using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using KSeF.Functions.Models;

namespace KSeF.Functions.Services;

public class JpkV7MBuilder
{
    private static readonly XNamespace Ns = "http://crd.gov.pl/wzor/2021/12/27/11148/";
    private static readonly XNamespace Etd = "http://crd.gov.pl/xml/schematy/dziedzinowe/mf/2021/06/08/eD/DefinicjeTypy/";

    public (string xml, int salesCount, decimal taxDue) Build(JpkV7MRequest req)
    {
        var root = new XElement(Ns + "JPK",
            new XAttribute(XNamespace.Xmlns + "tns", Ns.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "etd", Etd.NamespaceName)
        );

        // Naglowek (Header)
        root.Add(BuildNaglowek(req));

        // Podmiot1 (Taxpayer)
        root.Add(BuildPodmiot1(req.Company));

        // Deklaracja (Declaration)
        var (deklaracja, taxDue) = BuildDeklaracja(req);
        root.Add(deklaracja);

        // Ewidencja (Records)
        var (ewidencja, salesCount) = BuildEwidencja(req);
        root.Add(ewidencja);

        // Serialize to string with UTF-8
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            OmitXmlDeclaration = false
        };

        using var ms = new MemoryStream();
        using (var writer = XmlWriter.Create(ms, settings))
        {
            root.Save(writer);
        }

        var xml = Encoding.UTF8.GetString(ms.ToArray());
        return (xml, salesCount, taxDue);
    }

    private XElement BuildNaglowek(JpkV7MRequest req)
    {
        var celZlozenia = req.Purpose == "Correction" ? 2 : 1;

        return new XElement(Ns + "Naglowek",
            new XElement(Ns + "KodFormularza",
                new XAttribute("kodSystemowy", "JPK_V7M (2)"),
                new XAttribute("wersjaSchemy", "1-0E"),
                "JPK_VAT"
            ),
            new XElement(Ns + "WariantFormularza", 2),
            new XElement(Ns + "DataWytworzeniaJPK", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")),
            new XElement(Ns + "NazwaSystemu", "KPHG KSeF Integration"),
            new XElement(Ns + "CelZlozenia",
                new XAttribute("ppiVersion", "1-0E"),
                celZlozenia
            ),
            new XElement(Ns + "KodUrzedu", "0271"),
            new XElement(Ns + "Rok", req.Year),
            new XElement(Ns + "Miesiac", req.Month)
        );
    }

    private XElement BuildPodmiot1(CompanyData company)
    {
        var osoba = new XElement(Ns + "OsobaNiefizyczna",
            new XElement(Ns + "NIP", company.Nip),
            new XElement(Ns + "PelnaNazwa", company.Name),
            new XElement(Ns + "Email", string.IsNullOrWhiteSpace(company.Email) ? "biuro@firma.pl" : company.Email)
        );

        if (!string.IsNullOrWhiteSpace(company.Phone))
            osoba.Add(new XElement(Ns + "Telefon", company.Phone));

        return new XElement(Ns + "Podmiot1", osoba);
    }

    private (XElement deklaracja, decimal taxDue) BuildDeklaracja(JpkV7MRequest req)
    {
        // Calculate totals from sales records
        decimal totalNet23 = 0, totalVat23 = 0;
        decimal totalNet8 = 0, totalVat8 = 0;
        decimal totalNet5 = 0, totalVat5 = 0;
        decimal totalNet0 = 0;

        foreach (var rec in req.SalesRecords)
        {
            var vatRate = rec.NetAmount != 0 ? Math.Round(rec.VatAmount / rec.NetAmount * 100, 0) : 0;
            if (vatRate >= 22)
            {
                totalNet23 += rec.NetAmount;
                totalVat23 += rec.VatAmount;
            }
            else if (vatRate >= 7)
            {
                totalNet8 += rec.NetAmount;
                totalVat8 += rec.VatAmount;
            }
            else if (vatRate >= 4)
            {
                totalNet5 += rec.NetAmount;
                totalVat5 += rec.VatAmount;
            }
            else
            {
                totalNet0 += rec.NetAmount;
            }
        }

        var totalTaxDue = totalVat23 + totalVat8 + totalVat5;
        var totalNet = totalNet23 + totalNet8 + totalNet5 + totalNet0;

        var pozycje = new XElement(Ns + "PozycjeSzczegolowe",
            new XElement(Ns + "P_10", Fmt(totalNet0)),
            new XElement(Ns + "P_11", Fmt(totalNet5)),
            new XElement(Ns + "P_12", Fmt(totalVat5)),
            new XElement(Ns + "P_13", Fmt(totalNet8)),
            new XElement(Ns + "P_14", Fmt(totalVat8)),
            new XElement(Ns + "P_15", Fmt(totalNet23)),
            new XElement(Ns + "P_16", Fmt(totalVat23)),
            new XElement(Ns + "P_17", Fmt(0)),
            new XElement(Ns + "P_18", Fmt(0)),
            new XElement(Ns + "P_19", Fmt(0)),
            new XElement(Ns + "P_20", Fmt(0)),
            new XElement(Ns + "P_21", Fmt(0)),
            new XElement(Ns + "P_22", Fmt(0)),
            new XElement(Ns + "P_23", Fmt(totalNet)),
            new XElement(Ns + "P_24", Fmt(totalTaxDue)),
            new XElement(Ns + "P_25", Fmt(0)),
            new XElement(Ns + "P_26", Fmt(0)),
            new XElement(Ns + "P_27", Fmt(0)),
            new XElement(Ns + "P_28", Fmt(0)),
            new XElement(Ns + "P_29", Fmt(0)),
            new XElement(Ns + "P_30", Fmt(0)),
            new XElement(Ns + "P_31", Fmt(0)),
            new XElement(Ns + "P_32", Fmt(0)),
            new XElement(Ns + "P_33", Fmt(0)),
            new XElement(Ns + "P_34", Fmt(0)),
            new XElement(Ns + "P_35", Fmt(0)),
            new XElement(Ns + "P_36", Fmt(0)),
            new XElement(Ns + "P_37", Fmt(totalTaxDue)),
            new XElement(Ns + "P_38", Fmt(totalTaxDue)),
            new XElement(Ns + "P_39", Fmt(0)),
            new XElement(Ns + "P_40", Fmt(0)),
            new XElement(Ns + "P_41", Fmt(0)),
            new XElement(Ns + "P_42", Fmt(0)),
            new XElement(Ns + "P_43", Fmt(0)),
            new XElement(Ns + "P_44", Fmt(0)),
            new XElement(Ns + "P_45", Fmt(0)),
            new XElement(Ns + "P_46", Fmt(0)),
            new XElement(Ns + "P_47", Fmt(0)),
            new XElement(Ns + "P_48", Fmt(0)),
            new XElement(Ns + "P_49", Fmt(0)),
            new XElement(Ns + "P_50", Fmt(0)),
            new XElement(Ns + "P_51", Fmt(totalTaxDue)),
            new XElement(Ns + "P_52", Fmt(0)),
            new XElement(Ns + "P_53", Fmt(totalTaxDue)),
            new XElement(Ns + "P_54", Fmt(totalTaxDue)),
            new XElement(Ns + "P_540", 1),
            new XElement(Ns + "P_55", 1),
            new XElement(Ns + "P_56", 1),
            new XElement(Ns + "P_560", 1),
            new XElement(Ns + "P_57", 1),
            new XElement(Ns + "P_58", 1),
            new XElement(Ns + "P_59", 1),
            new XElement(Ns + "P_60", 2),
            new XElement(Ns + "P_61", 2),
            new XElement(Ns + "P_62", 2),
            new XElement(Ns + "P_63", 1),
            new XElement(Ns + "P_64", 1),
            new XElement(Ns + "P_65", 1),
            new XElement(Ns + "P_66", 1),
            new XElement(Ns + "P_660", 1),
            new XElement(Ns + "P_67", 1),
            new XElement(Ns + "P_68", Fmt(totalTaxDue)),
            new XElement(Ns + "P_69", 1)
        );

        var deklaracja = new XElement(Ns + "Deklaracja",
            new XElement(Ns + "Naglowek",
                new XElement(Ns + "KodFormularzaDekl",
                    new XAttribute("kodSystemowy", "VAT-7 (22)"),
                    new XAttribute("kodPodatku", "VAT"),
                    new XAttribute("rodzajZobowiazania", "Z"),
                    new XAttribute("wersjaSchemy", "1-0E"),
                    "VAT-7"
                ),
                new XElement(Ns + "WariantFormularzaDekl", 22)
            ),
            pozycje
        );

        return (deklaracja, totalTaxDue);
    }

    private (XElement ewidencja, int salesCount) BuildEwidencja(JpkV7MRequest req)
    {
        var ewidencja = new XElement(Ns + "Ewidencja");
        int lineNo = 0;
        decimal totalOutputVat = 0;

        foreach (var rec in req.SalesRecords)
        {
            lineNo++;
            var wiersz = new XElement(Ns + "SprzedazWiersz",
                new XElement(Ns + "LpSprzedazy", lineNo)
            );

            // KodKrajuNadaniaTIN must come before NrKontrahenta per schema
            if (!string.IsNullOrWhiteSpace(rec.CountryCode) && rec.CountryCode != "PL")
                wiersz.Add(new XElement(Ns + "KodKrajuNadaniaTIN", rec.CountryCode));

            wiersz.Add(new XElement(Ns + "NrKontrahenta", string.IsNullOrWhiteSpace(rec.CounterpartyNip) ? "BRAK" : rec.CounterpartyNip));
            wiersz.Add(new XElement(Ns + "NazwaKontrahenta", rec.CounterpartyName));
            wiersz.Add(new XElement(Ns + "DowodSprzedazy", rec.InvoiceNumber));
            wiersz.Add(new XElement(Ns + "DataWystawienia", rec.IssueDate));

            if (!string.IsNullOrWhiteSpace(rec.SaleDate))
                wiersz.Add(new XElement(Ns + "DataSprzedazy", rec.SaleDate));

            // KorektaPodstawyOpodt
            wiersz.Add(new XElement(Ns + "KorektaPodstawyOpodt", 1));

            // Payment dates
            if (!string.IsNullOrWhiteSpace(rec.PaymentDueDate))
            {
                wiersz.Add(new XElement(Ns + "TerminPlatnosci", rec.PaymentDueDate));
            }

            // VAT amounts by rate — simplified: put all in K_19 (net 23%) and K_20 (vat 23%)
            var vatRate = rec.NetAmount != 0 ? Math.Round(rec.VatAmount / rec.NetAmount * 100, 0) : 0;
            if (vatRate >= 22)
            {
                wiersz.Add(new XElement(Ns + "K_19", Fmt(rec.NetAmount)));
                wiersz.Add(new XElement(Ns + "K_20", Fmt(rec.VatAmount)));
            }
            else if (vatRate >= 7)
            {
                wiersz.Add(new XElement(Ns + "K_17", Fmt(rec.NetAmount)));
                wiersz.Add(new XElement(Ns + "K_18", Fmt(rec.VatAmount)));
            }
            else if (vatRate >= 4)
            {
                wiersz.Add(new XElement(Ns + "K_15", Fmt(rec.NetAmount)));
                wiersz.Add(new XElement(Ns + "K_16", Fmt(rec.VatAmount)));
            }
            else
            {
                wiersz.Add(new XElement(Ns + "K_10", Fmt(rec.NetAmount)));
            }

            totalOutputVat += rec.VatAmount;
            ewidencja.Add(wiersz);
        }

        // SprzedazCtrl
        ewidencja.Add(new XElement(Ns + "SprzedazCtrl",
            new XElement(Ns + "LiczbaWierszySprzedazy", lineNo),
            new XElement(Ns + "PodatekNalezny", Fmt(totalOutputVat))
        ));

        // ZakupCtrl (no purchases)
        ewidencja.Add(new XElement(Ns + "ZakupCtrl",
            new XElement(Ns + "LiczbaWierszyZakupow", 0),
            new XElement(Ns + "PodatekNaliczony", Fmt(0))
        ));

        return (ewidencja, lineNo);
    }

    private static string Fmt(decimal value) =>
        value.ToString("F2", CultureInfo.InvariantCulture);
}
