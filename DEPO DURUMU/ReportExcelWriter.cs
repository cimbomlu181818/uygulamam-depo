using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows;

namespace DEPO_DURUMU
{
    /// <summary>
    /// Raporu .xlsx dosyası olarak yazar (zip + XML; ek kütüphane gerekmez, depo dışa aktarımıyla aynı yöntem).
    /// İki sayfa yazılır: "Rapor" (işlem tablosu) ve "Bilgi" (rapor adı, aralık, özet).
    /// Seri no gibi uzun değerler Excel'de bozulmasın diye hücreler metin olarak yazılır; sadece "No" sayıdır.
    /// </summary>
    public static class ReportExcelWriter
    {
        private static readonly string[] Headers =
            { "No", "Zaman", "İşlem", "Cins", "Ürün / Kalem", "Kişi / Birim", "Detay" };

        // ---------- KAYDET (pencereli) ----------

        /// <summary>Dosya adını sorar ve raporu Excel olarak kaydeder. Kaydedildiyse true döner.</summary>
        public static bool SaveWithDialog(Window owner, ReportData data)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Raporu Excel Olarak Kaydet",
                FileName = SafeFileName(data) + ".xlsx",
                Filter = "Excel dosyası (*.xlsx)|*.xlsx"
            };

            if (dialog.ShowDialog(owner) != true)
            {
                return false;
            }

            try
            {
                Save(dialog.FileName, data);
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner,
                    "Dosya yazılamadı: " + ex.Message + "\n\nDosya Excel'de açıksa kapatıp tekrar dene.",
                    "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            MessageBox.Show(owner, "Rapor Excel olarak kaydedildi:\n" + dialog.FileName, "Depo Durumu",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }

        /// <summary>Dosya adında kullanılamayan karakterleri atarak rapor adından dosya adı üretir.</summary>
        private static string SafeFileName(ReportData data)
        {
            var name = string.IsNullOrWhiteSpace(data.Title)
                ? "Rapor_" + data.RangeFrom.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                : data.Title.Trim();

            foreach (var bad in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(bad, '_');
            }

            return name;
        }

        // ---------- YAZ ----------

        /// <summary>Raporu verilen yola .xlsx olarak yazar. Var olan dosyanın üzerine yazar.</summary>
        public static void Save(string path, ReportData data)
        {
            var rows = new List<string[]> { Headers };
            for (var i = 0; i < data.Rows.Count; i++)
            {
                var r = data.Rows[i];
                rows.Add(new[]
                {
                    (i + 1).ToString(CultureInfo.InvariantCulture),
                    r.EventAtText, r.Operation, r.TypeName, r.Item, r.Person, r.Detail
                });
            }

            var info = new List<string[]>
            {
                new[] { "Alan", "Değer" },
                new[] { "Rapor adı", data.Title ?? "" },
                new[] { "Tarih aralığı", data.RangeText },
                new[] { "Hazırlanma zamanı", data.CreatedAt.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) },
                new[] { "Hazırlayan", data.CreatedBy ?? "" },
                new[] { "Toplam kayıt", data.Rows.Count.ToString(CultureInfo.InvariantCulture) }
            };

            foreach (var group in data.Rows.GroupBy(r => r.Operation)
                         .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                info.Add(new[] { "İşlem: " + group.Key, group.Count().ToString(CultureInfo.InvariantCulture) });
            }

            var sheets = new List<Sheet>
            {
                new Sheet { Name = "Rapor", Rows = rows, NumericColumns = new HashSet<int> { 0 }, Filter = true },
                new Sheet { Name = "Bilgi", Rows = info, NumericColumns = new HashSet<int>(), Filter = false }
            };

            WriteXlsx(path, sheets);
        }

        private class Sheet
        {
            public string Name;
            public List<string[]> Rows;                  // ilk satır başlıktır
            public HashSet<int> NumericColumns;          // sayı olarak yazılacak sütunlar
            public bool Filter;                          // başlık satırına süzgeç konsun mu
        }

        private static void WriteXlsx(string path, List<Sheet> sheets)
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                const string xmlHeader = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>";
                const string mainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                const string relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
                const string pkgRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

                var types = new StringBuilder(xmlHeader);
                types.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
                types.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
                types.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
                types.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
                types.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
                for (var i = 0; i < sheets.Count; i++)
                {
                    types.Append("<Override PartName=\"/xl/worksheets/sheet" + (i + 1) +
                                 ".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
                }
                types.Append("</Types>");
                AddZipText(zip, "[Content_Types].xml", types.ToString());

                AddZipText(zip, "_rels/.rels",
                    xmlHeader + "<Relationships xmlns=\"" + pkgRelNs + "\">" +
                    "<Relationship Id=\"rId1\" Type=\"" + relNs + "/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                    "</Relationships>");

                var workbook = new StringBuilder(xmlHeader);
                workbook.Append("<workbook xmlns=\"" + mainNs + "\" xmlns:r=\"" + relNs + "\"><sheets>");
                for (var i = 0; i < sheets.Count; i++)
                {
                    workbook.Append("<sheet name=\"" + XmlText(sheets[i].Name) + "\" sheetId=\"" + (i + 1) +
                                    "\" r:id=\"rId" + (i + 1) + "\"/>");
                }
                workbook.Append("</sheets></workbook>");
                AddZipText(zip, "xl/workbook.xml", workbook.ToString());

                var workbookRels = new StringBuilder(xmlHeader);
                workbookRels.Append("<Relationships xmlns=\"" + pkgRelNs + "\">");
                for (var i = 0; i < sheets.Count; i++)
                {
                    workbookRels.Append("<Relationship Id=\"rId" + (i + 1) + "\" Type=\"" + relNs +
                                        "/worksheet\" Target=\"worksheets/sheet" + (i + 1) + ".xml\"/>");
                }
                workbookRels.Append("<Relationship Id=\"rId" + (sheets.Count + 1) + "\" Type=\"" + relNs +
                                    "/styles\" Target=\"styles.xml\"/>");
                workbookRels.Append("</Relationships>");
                AddZipText(zip, "xl/_rels/workbook.xml.rels", workbookRels.ToString());

                // Stiller: 0 = normal, 1 = kalın başlık (açık mavi zemin)
                AddZipText(zip, "xl/styles.xml",
                    xmlHeader + "<styleSheet xmlns=\"" + mainNs + "\">" +
                    "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font>" +
                    "<font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
                    "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill>" +
                    "<fill><patternFill patternType=\"gray125\"/></fill>" +
                    "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFD9E1F2\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
                    "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                    "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                    "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
                    "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"/></cellXfs>" +
                    "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
                    "</styleSheet>");

                for (var i = 0; i < sheets.Count; i++)
                {
                    AddZipText(zip, "xl/worksheets/sheet" + (i + 1) + ".xml", BuildSheetXml(sheets[i], xmlHeader, mainNs));
                }
            }
        }

        private static string BuildSheetXml(Sheet sheet, string xmlHeader, string mainNs)
        {
            var rows = sheet.Rows;
            var columnCount = rows[0].Length;

            var xml = new StringBuilder(xmlHeader);
            xml.Append("<worksheet xmlns=\"" + mainNs + "\">");
            xml.Append("<sheetViews><sheetView workbookViewId=\"0\">" +
                       "<pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/>" +
                       "</sheetView></sheetViews>");

            // Sütun genişlikleri: içeriğe göre (en az 8, en çok 50).
            xml.Append("<cols>");
            for (var c = 0; c < columnCount; c++)
            {
                var longest = 0;
                for (var r = 0; r < rows.Count && r < 300; r++)
                {
                    var text = c < rows[r].Length ? rows[r][c] : null;
                    if (text != null && text.Length > longest)
                    {
                        longest = text.Length;
                    }
                }

                var width = Math.Min(50, Math.Max(8, longest + 2));
                xml.Append("<col min=\"" + (c + 1) + "\" max=\"" + (c + 1) + "\" width=\"" + width + "\" customWidth=\"1\"/>");
            }
            xml.Append("</cols>");

            xml.Append("<sheetData>");
            for (var r = 0; r < rows.Count; r++)
            {
                xml.Append("<row r=\"" + (r + 1) + "\">");

                for (var c = 0; c < columnCount; c++)
                {
                    var value = c < rows[r].Length ? (rows[r][c] ?? "") : "";
                    if (r > 0 && value.Length == 0)
                    {
                        continue;   // boş hücre yazılmaz
                    }

                    var reference = ColumnLetter(c) + (r + 1);

                    int number;
                    if (r > 0 && sheet.NumericColumns.Contains(c) &&
                        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number))
                    {
                        xml.Append("<c r=\"" + reference + "\"><v>" + number.ToString(CultureInfo.InvariantCulture) + "</v></c>");
                    }
                    else
                    {
                        xml.Append("<c r=\"" + reference + "\" t=\"inlineStr\"" + (r == 0 ? " s=\"1\"" : "") +
                                   "><is><t xml:space=\"preserve\">" + XmlText(value) + "</t></is></c>");
                    }
                }

                xml.Append("</row>");
            }
            xml.Append("</sheetData>");

            if (sheet.Filter)
            {
                xml.Append("<autoFilter ref=\"A1:" + ColumnLetter(columnCount - 1) + rows.Count + "\"/>");
            }

            xml.Append("</worksheet>");
            return xml.ToString();
        }

        private static string ColumnLetter(int index)
        {
            var letters = "";
            index++;
            while (index > 0)
            {
                var remainder = (index - 1) % 26;
                letters = (char)('A' + remainder) + letters;
                index = (index - 1) / 26;
            }

            return letters;
        }

        private static void AddZipText(ZipArchive zip, string entryName, string text)
        {
            var entry = zip.CreateEntry(entryName);
            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
            {
                writer.Write(text);
            }
        }

        /// <summary>XML içine yazılacak metni hazırlar: geçersiz karakterleri atar, özel işaretleri kaçırır.</summary>
        private static string XmlText(string value)
        {
            var builder = new StringBuilder((value ?? "").Length);

            foreach (var ch in value ?? "")
            {
                if (ch < ' ' && ch != '\t' && ch != '\n' && ch != '\r')
                {
                    continue;   // XML'de geçersiz denetim karakterleri
                }

                switch (ch)
                {
                    case '&': builder.Append("&amp;"); break;
                    case '<': builder.Append("&lt;"); break;
                    case '>': builder.Append("&gt;"); break;
                    case '"': builder.Append("&quot;"); break;
                    default: builder.Append(ch); break;
                }
            }

            return builder.ToString();
        }
    }
}