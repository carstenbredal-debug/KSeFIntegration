using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using KSeF.Functions.Models;

namespace KSeF.Functions.Services;

public class JpkV7MBuilder
{
    private static readonly XNamespace Ns = "http://crd.gov.pl/wzor/2025/12/19/14090/";
    private static readonly XNamespace Etd = "http://crd.gov.pl/xml/schematy/dziedzinowe/mf/2022/09/13/eD/DefinicjeTypy/";

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
                new XAttribute("kodSystemowy", "JPK_V7M (3)"),
                new XAttribute("wersjaSchemy", "1-0E"),
                "JPK_VAT"
            ),
            new XElement(Ns + "WariantFormularza", 3),
            new XElement(Ns + "DataWytworzeniaJPK", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")),
            new XElement(Ns + "NazwaSystemu", "KPHG KSeF Integration"),
            new XElement(Ns + "CelZlozenia",
                new XAttribute("poz", "P_7"),
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

        return new XElement(Ns + "Podmiot1",
            new XAttribute("rola", "Podatnik"),
            osoba);
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
            new XElement(Ns + "P_10", FmtInt(totalNet0)),
            new XElement(Ns + "P_11", FmtInt(totalNet5)),
            new XElement(Ns + "P_12", FmtInt(totalVat5)),
            new XElement(Ns + "P_13", FmtInt(totalNet8)),
            new XElement(Ns + "P_14", FmtInt(totalVat8)),
            new XElement(Ns + "P_15", FmtInt(totalNet23)),
            new XElement(Ns + "P_16", FmtInt(totalVat23)),
            new XElement(Ns + "P_17", FmtInt(0)),
            new XElement(Ns + "P_18", FmtInt(0)),
            new XElement(Ns + "P_19", FmtInt(0)),
            new XElement(Ns + "P_20", FmtInt(0)),
            new XElement(Ns + "P_21", FmtInt(0)),
            new XElement(Ns + "P_22", FmtInt(0)),
            new XElement(Ns + "P_23", FmtInt(totalNet)),
            new XElement(Ns + "P_24", FmtInt(totalTaxDue)),
            new XElement(Ns + "P_25", FmtInt(0)),
            new XElement(Ns + "P_26", FmtInt(0)),
            new XElement(Ns + "P_27", FmtInt(0)),
            new XElement(Ns + "P_28", FmtInt(0)),
            new XElement(Ns + "P_29", FmtInt(0)),
            new XElement(Ns + "P_30", FmtInt(0)),
            new XElement(Ns + "P_31", FmtInt(0)),
            new XElement(Ns + "P_32", FmtInt(0)),
            new XElement(Ns + "P_33", FmtInt(0)),
            new XElement(Ns + "P_34", FmtInt(0)),
            new XElement(Ns + "P_35", FmtInt(0)),
            new XElement(Ns + "P_36", FmtInt(0)),
            new XElement(Ns + "P_37", FmtInt(totalTaxDue)),
            new XElement(Ns + "P_38", FmtInt(totalTaxDue)),
            new XElement(Ns + "P_39", FmtInt(0)),
            new XElement(Ns + "P_40", FmtInt(0)),
            new XElement(Ns + "P_41", FmtInt(0)),
            new XElement(Ns + "P_42", FmtInt(0)),
            new XElement(Ns + "P_43", FmtInt(0)),
            new XElement(Ns + "P_44", FmtInt(0)),
            new XElement(Ns + "P_45", FmtInt(0)),
            new XElement(Ns + "P_46", FmtInt(0)),
            new XElement(Ns + "P_47", FmtInt(0)),
            new XElement(Ns + "P_48", FmtInt(0)),
            new XElement(Ns + "P_49", FmtInt(0)),
            new XElement(Ns + "P_50", FmtInt(0)),
            new XElement(Ns + "P_51", FmtInt(totalTaxDue)),
            new XElement(Ns + "P_52", FmtInt(0)),
            new XElement(Ns + "P_53", FmtInt(totalTaxDue)),
            new XElement(Ns + "P_54", FmtInt(totalTaxDue)),
            // P_540/P_55/P_56/P_560/P_58 is a choice — pick one refund term
            new XElement(Ns + "P_540", 1),
            // P_59/P_60/P_61 optional group (credit to future obligations) — omitted
            new XElement(Ns + "P_68", FmtInt(0)),
            new XElement(Ns + "P_69", FmtInt(0))
        );

        var deklaracja = new XElement(Ns + "Deklaracja",
            new XElement(Ns + "Naglowek",
                new XElement(Ns + "KodFormularzaDekl",
                    new XAttribute("kodSystemowy", "VAT-7 (23)"),
                    new XAttribute("kodPodatku", "VAT"),
                    new XAttribute("rodzajZobowiazania", "Z"),
                    new XAttribute("wersjaSchemy", "1-0E"),
                    "VAT-7"
                ),
                new XElement(Ns + "WariantFormularzaDekl", 23)
            ),
            pozycje,
            new XElement(Ns + "Pouczenia", 1)
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

            // KSeF choice (required in v3): NrKSeF, OFF, BFK, or DI
            if (!string.IsNullOrEmpty(rec.KSeFReferenceNumber))
                wiersz.Add(new XElement(Ns + "NrKSeF", rec.KSeFReferenceNumber));
            else
                wiersz.Add(new XElement(Ns + "BFK", 1));

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

    private static string FmtInt(decimal value) =>
        Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("F0", CultureInfo.InvariantCulture);
}
