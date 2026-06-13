using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using KSeF.Functions.Models;

namespace KSeF.Functions.Services;

/// <summary>
/// Builds KSeF-compliant FA(2) XML invoice documents.
/// Schema: http://crd.gov.pl/wzor/2023/06/29/12648/
/// </summary>
public class InvoiceXmlBuilder
{
    private static readonly XNamespace Ns = "http://crd.gov.pl/wzor/2023/06/29/12648/";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    public string Build(InvoiceData invoice)
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            BuildFaktura(invoice)
        );

        using var ms = new MemoryStream();
        using var xw = XmlWriter.Create(ms, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true
        });
        doc.Save(xw);
        xw.Flush();
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private XElement BuildFaktura(InvoiceData inv)
    {
        var faktura = new XElement(Ns + "Faktura",
            new XAttribute(XNamespace.Xmlns + "xsi", Xsi),
            new XAttribute(Xsi + "schemaLocation", "http://crd.gov.pl/wzor/2023/06/29/12648/ http://crd.gov.pl/wzor/2023/06/29/12648/schemat.xsd"),

            // Naglowek (Header)
            BuildNaglowek(inv),

            // Podmiot1 (Seller)
            BuildPodmiot1(inv.Seller),

            // Podmiot2 (Buyer)
            BuildPodmiot2(inv.Buyer),

            // Fa (Invoice body)
            BuildFa(inv)
        );

        return faktura;
    }

    private XElement BuildNaglowek(InvoiceData inv)
    {
        return new XElement(Ns + "Naglowek",
            new XElement(Ns + "KodFormularza",
                new XAttribute("kodSystemowy", "FA (2)"),
                new XAttribute("wersjaSchemy", "1-0E"),
                "FA"),
            new XElement(Ns + "WariantFormularza", 2),
            new XElement(Ns + "DataWytworzeniaFa", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")),
            new XElement(Ns + "SystemInfo", "KSeFIntegration/1.0")
        );
    }

    private XElement BuildPodmiot1(SellerData seller)
    {
        var countryCode = string.IsNullOrWhiteSpace(seller.CountryCode) ? "PL" : seller.CountryCode;
        return new XElement(Ns + "Podmiot1",
            new XElement(Ns + "DaneIdentyfikacyjne",
                new XElement(Ns + "NIP", CleanNip(seller.NIP)),
                new XElement(Ns + "Nazwa", seller.Name)
            ),
            new XElement(Ns + "Adres",
                new XElement(Ns + "KodKraju", countryCode),
                new XElement(Ns + "AdresL1", FormatAddress(seller.Street, seller.BuildingNumber, seller.ApartmentNumber)),
                new XElement(Ns + "AdresL2", $"{seller.PostalCode} {seller.City}")
            )
        );
    }

    private static readonly HashSet<string> EuCountryCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AT", "BE", "BG", "HR", "CY", "CZ", "DK", "EE", "FI", "FR",
        "DE", "GR", "HU", "IE", "IT", "LV", "LT", "LU", "MT", "NL",
        "PT", "RO", "SK", "SI", "ES", "SE"
    };

    private XElement BuildPodmiot2(BuyerData buyer)
    {
        var countryCode = string.IsNullOrWhiteSpace(buyer.CountryCode) ? "PL" : buyer.CountryCode.Trim().ToUpperInvariant();
        var isPolish = countryCode == "PL";
        var isEu = !isPolish && EuCountryCodes.Contains(countryCode);
        var vatNumber = buyer.NIP?.Trim() ?? "";

        var identyfikacyjne = new XElement(Ns + "DaneIdentyfikacyjne");

        if (isPolish)
        {
            identyfikacyjne.Add(new XElement(Ns + "NIP", CleanNip(vatNumber)));
        }
        else if (isEu && !string.IsNullOrWhiteSpace(vatNumber))
        {
            // Strip country prefix if present (e.g., "DK12345678" -> "12345678")
            var cleanVat = vatNumber;
            if (cleanVat.Length > 2 && char.IsLetter(cleanVat[0]) && char.IsLetter(cleanVat[1]))
                cleanVat = cleanVat[2..];
            identyfikacyjne.Add(new XElement(Ns + "KodUE", countryCode));
            identyfikacyjne.Add(new XElement(Ns + "NrVatUE", cleanVat));
        }
        else
        {
            identyfikacyjne.Add(new XElement(Ns + "BrakID", 1));
        }

        identyfikacyjne.Add(new XElement(Ns + "Nazwa", buyer.Name));

        return new XElement(Ns + "Podmiot2",
            identyfikacyjne,
            new XElement(Ns + "Adres",
                new XElement(Ns + "KodKraju", countryCode),
                new XElement(Ns + "AdresL1", FormatAddress(buyer.Street, buyer.BuildingNumber, buyer.ApartmentNumber)),
                new XElement(Ns + "AdresL2", $"{buyer.PostalCode} {buyer.City}")
            )
        );
    }

    private XElement BuildFa(InvoiceData inv)
    {
        var currencyCode = string.IsNullOrWhiteSpace(inv.CurrencyCode) ? "PLN" : inv.CurrencyCode;
        var fa = new XElement(Ns + "Fa",
            new XElement(Ns + "KodWaluty", currencyCode),
            new XElement(Ns + "P_1", inv.IssueDate.ToString("yyyy-MM-dd")),
            new XElement(Ns + "P_2", inv.InvoiceNumber),
            new XElement(Ns + "P_6", inv.SaleDate?.ToString("yyyy-MM-dd") ?? inv.IssueDate.ToString("yyyy-MM-dd")),
            new XElement(Ns + "P_13_1", inv.Lines.Sum(l => l.NetAmount).ToString("F2", CultureInfo.InvariantCulture)),
            new XElement(Ns + "P_14_1", inv.Lines.Where(l => l.VatRate == 23).Sum(l => l.VatAmount).ToString("F2", CultureInfo.InvariantCulture)),
            new XElement(Ns + "P_15", inv.Lines.Sum(l => l.GrossAmount).ToString("F2", CultureInfo.InvariantCulture)),
            new XElement(Ns + "Adnotacje",
                new XElement(Ns + "P_16", 2),
                new XElement(Ns + "P_17", 2),
                new XElement(Ns + "P_18", 2),
                new XElement(Ns + "P_18A", 2),
                new XElement(Ns + "Zwolnienie",
                    new XElement(Ns + "P_19N", 1)
                ),
                new XElement(Ns + "NoweSrodkiTransportu",
                    new XElement(Ns + "P_22N", 1)
                ),
                new XElement(Ns + "P_23", 2),
                new XElement(Ns + "PMarzy",
                    new XElement(Ns + "P_PMarzyN", 1)
                )
            ),
            new XElement(Ns + "RodzajFaktury", "VAT")
        );

        // Invoice lines — individual FaWiersz elements directly inside Fa
        foreach (var line in inv.Lines)
        {
            fa.Add(new XElement(Ns + "FaWiersz",
                new XElement(Ns + "NrWierszaFa", line.LineNumber),
                new XElement(Ns + "P_7", line.Description),
                new XElement(Ns + "P_8A", line.UnitOfMeasure),
                new XElement(Ns + "P_8B", line.Quantity.ToString("F4", CultureInfo.InvariantCulture)),
                new XElement(Ns + "P_9A", line.UnitPrice.ToString("F2", CultureInfo.InvariantCulture)),
                new XElement(Ns + "P_11", line.NetAmount.ToString("F2", CultureInfo.InvariantCulture)),
                new XElement(Ns + "P_12", FormatVatRate(line.VatRate))
            ));
        }

        // Payment info
        if (inv.PaymentDueDate.HasValue)
        {
            fa.Add(new XElement(Ns + "Platnosc",
                new XElement(Ns + "TerminPlatnosci",
                    new XElement(Ns + "Termin", inv.PaymentDueDate.Value.ToString("yyyy-MM-dd"))
                ),
                new XElement(Ns + "FormaPlatnosci", MapPaymentMethod(inv.PaymentMethod)),
                !string.IsNullOrEmpty(inv.BankAccountNumber)
                    ? new XElement(Ns + "RachunekBankowy",
                        new XElement(Ns + "NrRB", inv.BankAccountNumber))
                    : null!
            ));
        }

        return fa;
    }

    private static string CleanNip(string nip)
    {
        var digits = new string(nip.Where(char.IsDigit).ToArray());
        // Polish NIP must be exactly 10 digits
        if (digits.Length > 10)
            digits = digits[..10];
        return digits;
    }

    private static string FormatAddress(string street, string building, string? apartment) =>
        string.IsNullOrEmpty(apartment)
            ? $"{street} {building}"
            : $"{street} {building}/{apartment}";

    private static string FormatVatRate(decimal rate) => rate switch
    {
        23 => "23",
        8 => "8",
        5 => "5",
        0 => "0",
        _ => rate.ToString("F0", CultureInfo.InvariantCulture)
    };

    private static string MapPaymentMethod(string method) => method.ToLower() switch
    {
        "transfer" or "bank transfer" => "6",
        "cash" => "1",
        "card" or "credit card" => "2",
        "cheque" or "check" => "3",
        _ => "6" // default to transfer
    };
}
