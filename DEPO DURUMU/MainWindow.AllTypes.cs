using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    // ---------- TÜM CİNSLERİ TEK CSV'YE DIŞA AKTAR / İÇE AKTAR ----------
    //
    // DIŞA AKTARMA: Tek tablo. Sütunlar: "Cins", "Sıra No", tüm cinslerdeki özelliklerin
    // birleşimi (her özellik bir sütun), "Adet". Her ürün bir satırdır; cinsler blok blok
    // alt alta gelir. Cinste olmayan özelliğin hücresi boş kalır.
    //
    // İÇE AKTARMA: Kullanıcıya dosyanın başlıkları gösterilir; hangisinin Cins, hangisinin
    // Seri No ve Sistem İsmi (sistem adı) sütunu olduğu sorulur. Depoda olmayan cinsler ve özellikler dosyadan otomatik
    // oluşturulur; var olan cinse dosyada dolu gelen yeni bir sütun da özellik olarak eklenir.
    // Cins hücresi boş olan satırlar atlanmaz; "Cinsi Belirsiz" adlı ortak cinse eklenir.
    // Yazmadan önce tek bir özet gösterilir ve onay istenir.
    public partial class MainWindow
    {
        private const string CinsHeader = "Cins";
        private const string SiraNoHeader = "Sıra No";
        private const string AdetHeader = "Adet";
        private const string SeriNoHeader = "Seri No";
        private const string SistemIsmiHeader = "Sistem İsmi";
        private const string TextDataType = "Metin";

        // İçe aktarmada Cins hücresi boş olan satırlar atlanmaz; bu adlı cinse eklenir.
        private const string UnknownTypeName = "Cinsi Belirsiz";

        // İçe aktarmada dosyadaki bir sütunun, uygulamadaki hangi özelliğe karşılık geldiği.
        private class ImportColumn
        {
            public int Index;
            public string Header;
            public string PropertyName;
            public bool IsSerial;                 // kullanıcının "seri numarası" diye seçtiği sütun
            public PropertyDefinition Property;   // null ise özellik henüz yok, oluşturulacak
            public string NewDataType;            // yeni özelliğin veri tipi

            public string DataType
            {
                get { return Property != null ? Property.DataType : NewDataType; }
            }

            public bool IsSerialNumberProperty
            {
                get { return Property != null ? Property.IsSerialNumber : IsSerial; }
            }
        }

        // İçe aktarmada dosyadaki bir cinsin satırları ve o cinse yapılacak eklemeler.
        private class ImportTypePlan
        {
            public string Name;
            public ProductType Existing;          // null ise cins depoda yok, oluşturulacak
            public List<string[]> Rows = new List<string[]>();
            public List<ImportColumn> AttachColumns = new List<ImportColumn>();   // cinse yeni bağlanacak özellikler
            public List<ImportColumn> WriteColumns = new List<ImportColumn>();    // ürün değeri yazılacak özellikler
        }

        private class ImportResult
        {
            public int Added;
            public List<string> CreatedTypes = new List<string>();
            public List<string> CreatedProperties = new List<string>();
            public List<string> AttachedToExistingTypes = new List<string>();
            public List<string> SkippedSerials = new List<string>();
            public List<string> InvalidYesNo = new List<string>();
            public string Failure;
        }

        // =====================================================================
        // DIŞA AKTAR
        // =====================================================================

        private void ExportAllTypesMenu_Click(object sender, RoutedEventArgs e)
        {
            var types = ProductTypeRepository.GetAll();

            // Özellik adları tekildir (PropertyDefinitions.Name UNIQUE); sütunlar cinslerdeki
            // özelliklerin birleşimidir, ilk görüldükleri sıraya göre dizilir.
            var propertiesByType = new Dictionary<int, List<PropertyDefinition>>();
            var columnNames = new List<string>();

            foreach (var type in types)
            {
                var properties = TypePropertyRepository.GetForType(type.Id);
                propertiesByType[type.Id] = properties;

                foreach (var property in properties)
                {
                    if (!columnNames.Contains(property.Name))
                    {
                        columnNames.Add(property.Name);
                    }
                }
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Tüm Grupları Dışa Aktar (CSV)",
                FileName = "Depo_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".csv",
                Filter = "CSV dosyası (*.csv)|*.csv"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var headers = new List<string> { CinsHeader, SiraNoHeader };
            headers.AddRange(columnNames);
            headers.Add(AdetHeader);

            var lines = new List<string> { string.Join(";", headers.Select(CsvEscape)) };
            var count = 0;

            foreach (var type in types)
            {
                var properties = propertiesByType[type.Id];
                var products = ProductRepository.GetForType(type.Id);
                var rank = 0;

                foreach (var product in products)
                {
                    rank++;
                    var values = ProductRepository.GetValues(product.Id);

                    var cells = new List<string> { type.Name, rank.ToString() };

                    foreach (var columnName in columnNames)
                    {
                        var property = properties.FirstOrDefault(p => p.Name == columnName);
                        var value = "";
                        if (property != null && values.ContainsKey(property.Id))
                        {
                            value = values[property.Id];
                        }
                        cells.Add(value);
                    }

                    cells.Add(product.Quantity.ToString());
                    lines.Add(string.Join(";", cells.Select(CsvEscape)));
                    count++;
                }
            }

            try
            {
                File.WriteAllLines(dialog.FileName, lines, new UTF8Encoding(true));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Dosya yazılamadı: " + ex.Message, "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            LogRepository.Add(null, count + " satır", "Dışa aktarıldı");

            MessageBox.Show(count + " ürün dışa aktarıldı.", "Depo Durumu",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // =====================================================================
        // EXCEL (.xlsx) DIŞA AKTAR: depo ve/veya hurda (ek kütüphane gerekmez)
        // =====================================================================
        //
        // Üç seçenek: Depo + Hurda (tek dosya, iki sayfa), sadece Depo, sadece Hurda.
        // Seri no gibi uzun değerler Excel'de bozulmasın diye metin olarak yazılır;
        // sadece "Sıra No" ve "Adet" sayı olarak yazılır.

        private const string ScrapDateHeader = "Hurdaya Taşınma Tarihi";

        private class ExportSheet
        {
            public string Name;
            public List<string[]> Rows;                                   // ilk satır başlıktır
            public HashSet<int> NumericColumns = new HashSet<int>();      // sayı olarak yazılacak sütunlar
        }

        private void ExportBothExcelMenu_Click(object sender, RoutedEventArgs e)
        {
            ExportExcel("Depo_Hurda", true, true);
        }

        private void ExportDepotExcelMenu_Click(object sender, RoutedEventArgs e)
        {
            ExportExcel("Depo", true, false);
        }

        private void ExportScrapExcelMenu_Click(object sender, RoutedEventArgs e)
        {
            ExportExcel("Hurda", false, true);
        }

        private void ExportExcel(string fileBaseName, bool includeDepot, bool includeScrap)
        {
            var sheets = new List<ExportSheet>();
            var depotCount = 0;
            var scrapCount = 0;

            try
            {
                if (includeDepot)
                {
                    sheets.Add(BuildDepotSheet(out depotCount));
                }
                if (includeScrap)
                {
                    sheets.Add(BuildScrapSheet(out scrapCount));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Veriler okunamadı: " + ex.Message, "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (depotCount + scrapCount == 0)
            {
                MessageBox.Show("Dışa aktarılacak ürün yok.", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Excel'e Dışa Aktar",
                FileName = fileBaseName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".xlsx",
                Filter = "Excel dosyası (*.xlsx)|*.xlsx"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                WriteXlsx(dialog.FileName, sheets);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Dosya yazılamadı: " + ex.Message + "\n\nDosya Excel'de açıksa kapatıp tekrar dene.",
                    "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (includeDepot)
            {
                LogRepository.Add(null, depotCount + " satır", "Dışa aktarıldı");
            }
            if (includeScrap)
            {
                LogRepository.Add(null, "Hurda: " + scrapCount + " satır", "Dışa aktarıldı");
            }

            string message;
            if (includeDepot && includeScrap)
            {
                message = depotCount + " depo ürünü (\"Depo\" sayfası) ve " + scrapCount +
                          " hurda ürünü (\"Hurda\" sayfası) dışa aktarıldı.";
            }
            else if (includeDepot)
            {
                message = depotCount + " depo ürünü dışa aktarıldı.";
            }
            else
            {
                message = scrapCount + " hurda ürünü dışa aktarıldı.";
            }

            MessageBox.Show(message, "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>Depodaki tüm ürünler: Grup, Sıra No, özellikler..., Adet.</summary>
        private ExportSheet BuildDepotSheet(out int count)
        {
            count = 0;

            var types = ProductTypeRepository.GetAll();
            var propertiesByType = new Dictionary<int, List<PropertyDefinition>>();
            var columnNames = new List<string>();

            foreach (var type in types)
            {
                var properties = TypePropertyRepository.GetForType(type.Id);
                propertiesByType[type.Id] = properties;

                foreach (var property in properties)
                {
                    if (!columnNames.Contains(property.Name))
                    {
                        columnNames.Add(property.Name);
                    }
                }
            }

            var headers = new List<string> { "Grup", SiraNoHeader };
            headers.AddRange(columnNames);
            headers.Add(AdetHeader);

            var sheet = new ExportSheet { Name = "Depo", Rows = new List<string[]>() };
            sheet.Rows.Add(headers.ToArray());
            sheet.NumericColumns.Add(1);
            sheet.NumericColumns.Add(headers.Count - 1);

            foreach (var type in types)
            {
                var properties = propertiesByType[type.Id];
                var products = ProductRepository.GetForType(type.Id);
                var rank = 0;

                foreach (var product in products)
                {
                    rank++;
                    var values = ProductRepository.GetValues(product.Id);

                    var cells = new List<string> { type.Name, rank.ToString() };

                    foreach (var columnName in columnNames)
                    {
                        var property = properties.FirstOrDefault(p => p.Name == columnName);
                        var value = "";
                        if (property != null && values.ContainsKey(property.Id))
                        {
                            value = values[property.Id];
                        }
                        cells.Add(value);
                    }

                    cells.Add(product.Quantity.ToString());
                    sheet.Rows.Add(cells.ToArray());
                    count++;
                }
            }

            return sheet;
        }

        /// <summary>Hurdadaki tüm ürünler: Grup, Sıra No, özellikler..., Adet, Hurdaya Taşınma Tarihi.</summary>
        private ExportSheet BuildScrapSheet(out int count)
        {
            count = 0;

            var items = ScrapRepository.GetAll()
                .OrderBy(i => i.TypeName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(i => i.OriginalSortOrder)
                .ThenBy(i => i.Id)
                .ToList();
            var valuesByItem = ScrapRepository.GetAllValuesGrouped();

            // Sütunlar hurdadaki değerlerin özellik adlarının birleşimidir (ilk görüldükleri sıraya göre).
            var columnNames = new List<string>();
            foreach (var item in items)
            {
                List<ScrapValue> itemValues;
                if (!valuesByItem.TryGetValue(item.Id, out itemValues))
                {
                    continue;
                }

                foreach (var value in itemValues)
                {
                    if (!columnNames.Contains(value.PropertyName))
                    {
                        columnNames.Add(value.PropertyName);
                    }
                }
            }

            var headers = new List<string> { "Grup", SiraNoHeader };
            headers.AddRange(columnNames);
            headers.Add(AdetHeader);
            headers.Add(ScrapDateHeader);

            var sheet = new ExportSheet { Name = "Hurda", Rows = new List<string[]>() };
            sheet.Rows.Add(headers.ToArray());
            sheet.NumericColumns.Add(1);
            sheet.NumericColumns.Add(headers.Count - 2);

            string currentGroup = null;
            var rank = 0;

            foreach (var item in items)
            {
                if (currentGroup == null ||
                    !string.Equals(currentGroup, item.TypeName, StringComparison.CurrentCultureIgnoreCase))
                {
                    currentGroup = item.TypeName;
                    rank = 0;
                }
                rank++;

                List<ScrapValue> itemValues;
                if (!valuesByItem.TryGetValue(item.Id, out itemValues))
                {
                    itemValues = new List<ScrapValue>();
                }

                var cells = new List<string> { item.TypeName, rank.ToString() };

                foreach (var columnName in columnNames)
                {
                    var found = itemValues.FirstOrDefault(v => v.PropertyName == columnName);
                    cells.Add(found != null ? (found.TextValue ?? "") : "");
                }

                cells.Add(item.Quantity.ToString());
                cells.Add(item.ScrappedAt ?? "");
                sheet.Rows.Add(cells.ToArray());
                count++;
            }

            return sheet;
        }

        /// <summary>Basit bir .xlsx dosyası yazar (zip + XML); ek kütüphane gerekmez.</summary>
        private static void WriteXlsx(string path, List<ExportSheet> sheets)
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                const string xmlHeader = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>";
                const string mainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                const string relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
                const string pkgRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

                // [Content_Types].xml
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

                // _rels/.rels
                AddZipText(zip, "_rels/.rels",
                    xmlHeader + "<Relationships xmlns=\"" + pkgRelNs + "\">" +
                    "<Relationship Id=\"rId1\" Type=\"" + relNs + "/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                    "</Relationships>");

                // xl/workbook.xml
                var workbook = new StringBuilder(xmlHeader);
                workbook.Append("<workbook xmlns=\"" + mainNs + "\" xmlns:r=\"" + relNs + "\"><sheets>");
                for (var i = 0; i < sheets.Count; i++)
                {
                    workbook.Append("<sheet name=\"" + XmlText(sheets[i].Name) + "\" sheetId=\"" + (i + 1) +
                                    "\" r:id=\"rId" + (i + 1) + "\"/>");
                }
                workbook.Append("</sheets></workbook>");
                AddZipText(zip, "xl/workbook.xml", workbook.ToString());

                // xl/_rels/workbook.xml.rels
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

                // xl/styles.xml: 0 = normal, 1 = kalın başlık (açık mavi zemin)
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

                // xl/worksheets/sheetN.xml
                for (var i = 0; i < sheets.Count; i++)
                {
                    AddZipText(zip, "xl/worksheets/sheet" + (i + 1) + ".xml", BuildSheetXml(sheets[i], xmlHeader, mainNs));
                }
            }
        }

        private static string BuildSheetXml(ExportSheet sheet, string xmlHeader, string mainNs)
        {
            var rows = sheet.Rows;
            var columnCount = rows[0].Length;

            var xml = new StringBuilder(xmlHeader);
            xml.Append("<worksheet xmlns=\"" + mainNs + "\">");
            xml.Append("<sheetViews><sheetView workbookViewId=\"0\">" +
                       "<pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/>" +
                       "</sheetView></sheetViews>");

            // Sütun genişlikleri: içeriğe göre (en az 10, en çok 45).
            xml.Append("<cols>");
            for (var c = 0; c < columnCount; c++)
            {
                var longest = 0;
                for (var r = 0; r < rows.Count && r < 300; r++)
                {
                    if (c < rows[r].Length && rows[r][c].Length > longest)
                    {
                        longest = rows[r][c].Length;
                    }
                }

                var width = Math.Min(45, Math.Max(10, longest + 2));
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

            xml.Append("<autoFilter ref=\"A1:" + ColumnLetter(columnCount - 1) + rows.Count + "\"/>");
            xml.Append("</worksheet>");
            return xml.ToString();
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
            var builder = new StringBuilder(value.Length);

            foreach (var ch in value)
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

        // =====================================================================
        // İÇE AKTAR
        // =====================================================================

        private void ImportAllTypesMenu_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Depoya İçe Aktar",
                Filter = "Excel veya CSV (*.xlsx;*.xlsm;*.csv)|*.xlsx;*.xlsm;*.csv|Tüm dosyalar (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            List<string[]> rows;
            try
            {
                rows = ReadTableFile(dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Dosya okunamadı: " + ex.Message, "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (rows == null)
            {
                return;   // kullanıcı sayfa seçiminden vazgeçti
            }

            if (rows.Count < 1)
            {
                MessageBox.Show("Dosyada okunacak satır bulunamadı.",
                    "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Önce kısa, örnekli bilgi ekranı.
            if (!ShowImportInfo("Depoya İçe Aktar", false))
            {
                return;
            }

            // Kullanıcı satırları ve sütunları kendisi ayarlar.
            int cinsColumn, serialColumn, systemColumn;
            rows = ShowImportSettings(rows, "Depoya İçe Aktar",
                out cinsColumn, out serialColumn, out systemColumn);
            if (rows == null)
            {
                return;
            }

            var headers = rows[0].Select(h => h.Trim()).ToArray();

            // Sütunları özelliklerle eşleştir, cinslere göre satırları grupla.
            var ignoredColumns = new List<string>();
            int quantityColumn;
            var columns = BuildImportColumns(rows, headers, cinsColumn, serialColumn, systemColumn, out quantityColumn, ignoredColumns);

            int blankTypeRows;
            var plans = BuildImportPlans(rows, cinsColumn, columns, out blankTypeRows);

            if (plans.Count == 0)
            {
                MessageBox.Show("Dosyada eklenecek ürün satırı bulunamadı.",
                    "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Yazmadan önce tek bir özet göster, onay iste.
            if (!ConfirmImport(plans, columns, serialColumn >= 0 ? headers[serialColumn] : null,
                systemColumn >= 0 ? headers[systemColumn] : null, blankTypeRows,
                BuildDuplicateSerialNote(rows, serialColumn)))
            {
                return;
            }

            var result = ExecuteImport(plans, columns, quantityColumn);

            if (result.Failure != null)
            {
                MessageBox.Show(this,
                    "İçe aktarma tamamlanamadı; hiçbir değişiklik yapılmadı.\n\nNeden: " + result.Failure,
                    "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Sonuç mesajı
            var message = new StringBuilder();
            message.AppendLine(result.Added + " ürün içe aktarıldı.");

            if (result.CreatedTypes.Count > 0)
            {
                message.AppendLine();
                message.AppendLine("Yeni oluşturulan gruplar: " + JoinLimited(result.CreatedTypes, 15));
            }
            if (result.CreatedProperties.Count > 0)
            {
                message.AppendLine();
                message.AppendLine("Yeni oluşturulan bilgiler: " + JoinLimited(result.CreatedProperties, 15));
            }
            if (result.AttachedToExistingTypes.Count > 0)
            {
                message.AppendLine();
                message.AppendLine("Var olan gruplara eklenen bilgiler: " + JoinLimited(result.AttachedToExistingTypes, 15));
            }
            if (result.SkippedSerials.Count > 0)
            {
                message.AppendLine();
                message.AppendLine("Seri no zaten kullanıldığı için eklenmeyenler (" + result.SkippedSerials.Count + "): " +
                                   JoinLimited(result.SkippedSerials, 15));
            }
            if (blankTypeRows > 0)
            {
                message.AppendLine();
                message.AppendLine("Grup hücresi boş olan " + blankTypeRows + " satır \"" + UnknownTypeName +
                                   "\" grubuna eklendi (seri no çakışanlar hariç).");
            }
            if (result.InvalidYesNo.Count > 0)
            {
                message.AppendLine();
                message.AppendLine("Evet/Hayır özelliğine uymayan değerler boş bırakıldı: " + JoinLimited(result.InvalidYesNo, 10));
            }
            if (ignoredColumns.Count > 0)
            {
                message.AppendLine();
                message.AppendLine("Yok sayılan sütunlar: " + JoinLimited(ignoredColumns, 10));
            }
            MessageBox.Show(this, message.ToString().TrimEnd(), "Depo Durumu",
                MessageBoxButton.OK, MessageBoxImage.Information);

            if (TypePage.Visibility == Visibility.Visible && _currentType != null)
            {
                LoadProductGrid(_currentType);
            }
            else
            {
                ShowHome();
            }
        }

        /// <summary>
        /// Dosyanın sütunlarını uygulamadaki özelliklerle eşleştirir. Cins, Sıra No ve Adet
        /// sütunları özellik sayılmaz. Hiç dolu hücresi olmayan sütunlar dışarıda bırakılır.
        /// </summary>
        private List<ImportColumn> BuildImportColumns(List<string[]> rows, string[] headers, int cinsColumn,
            int serialColumn, int systemColumn, out int quantityColumn, List<string> ignoredColumns)
        {
            quantityColumn = -1;

            var library = PropertyDefinitionRepository.GetAll();

            // Seri numarası sütununun yazılacağı özellik: uygulamadaki mevcut "Seri No".
            var serialProperty =
                library.FirstOrDefault(p => p.IsSerialNumber && string.Equals(p.Name, SeriNoHeader, StringComparison.OrdinalIgnoreCase))
                ?? library.FirstOrDefault(p => p.IsSerialNumber)
                ?? library.FirstOrDefault(p => string.Equals(p.Name, SeriNoHeader, StringComparison.OrdinalIgnoreCase));

            var serialName = serialProperty != null ? serialProperty.Name : SeriNoHeader;

            // Sistem adı sütununun yazılacağı özellik: uygulamadaki sabit "Sistem İsmi".
            var systemProperty = library.FirstOrDefault(p =>
                string.Equals(p.Name, SistemIsmiHeader, StringComparison.OrdinalIgnoreCase));
            var systemName = systemProperty != null ? systemProperty.Name : SistemIsmiHeader;

            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (serialColumn >= 0)
            {
                usedNames.Add(serialName);
            }
            if (systemColumn >= 0)
            {
                usedNames.Add(systemName);
            }

            var columns = new List<ImportColumn>();

            for (var i = 0; i < headers.Length; i++)
            {
                var header = headers[i];
                if (i == cinsColumn || header.Length == 0)
                {
                    continue;
                }

                if (i == serialColumn)
                {
                    columns.Add(new ImportColumn
                    {
                        Index = i,
                        Header = header,
                        PropertyName = serialName,
                        IsSerial = true,
                        Property = serialProperty,
                        NewDataType = TextDataType
                    });
                    continue;
                }

                if (i == systemColumn)
                {
                    columns.Add(new ImportColumn
                    {
                        Index = i,
                        Header = header,
                        PropertyName = systemName,
                        Property = systemProperty,
                        NewDataType = TextDataType
                    });
                    continue;
                }

                if (string.Equals(header, SiraNoHeader, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(header, AdetHeader, StringComparison.OrdinalIgnoreCase))
                {
                    if (quantityColumn < 0)
                    {
                        quantityColumn = i;
                    }
                    else
                    {
                        ignoredColumns.Add(header + " (ikinci Adet sütunu)");
                    }
                    continue;
                }

                if (usedNames.Contains(header))
                {
                    ignoredColumns.Add(header + " (aynı özelliğe giden başka bir sütun var)");
                    continue;
                }
                usedNames.Add(header);

                var existing = library.FirstOrDefault(p => string.Equals(p.Name, header, StringComparison.OrdinalIgnoreCase));

                columns.Add(new ImportColumn
                {
                    Index = i,
                    Header = header,
                    PropertyName = existing != null ? existing.Name : header,
                    Property = existing,
                    NewDataType = DetectDataType(rows, i)
                });
            }

            // Hiç dolu hücresi olmayan sütunlar için özellik açmanın anlamı yok.
            return columns.Where(c => ColumnHasValues(rows, c.Index)).ToList();
        }

        /// <summary>
        /// Cins sütununa göre satırları gruplar; her cins için hangi özelliklerin bağlanacağını hesaplar.
        /// Cins hücresi boş olan satırlar atlanmaz, "Cinsi Belirsiz" cinsine gruplanır.
        /// Hiçbir hücresinde veri olmayan satırlar (cins boş ve tüm özellik hücreleri boş) yok sayılır.
        /// </summary>
        private List<ImportTypePlan> BuildImportPlans(List<string[]> rows, int cinsColumn,
            List<ImportColumn> columns, out int blankTypeRows)
        {
            blankTypeRows = 0;

            var typesByName = new Dictionary<string, ProductType>(StringComparer.OrdinalIgnoreCase);
            foreach (var type in ProductTypeRepository.GetAll())
            {
                typesByName[type.Name] = type;
            }

            var plans = new List<ImportTypePlan>();
            var planByName = new Dictionary<string, ImportTypePlan>(StringComparer.OrdinalIgnoreCase);

            for (var r = 1; r < rows.Count; r++)
            {
                var cells = rows[r];
                var typeName = Cell(cells, cinsColumn);
                if (typeName.Length == 0)
                {
                    var rowHasData = columns.Any(c => Cell(cells, c.Index).Length > 0);
                    if (!rowHasData)
                    {
                        continue;   // tamamen boş satır: eklenecek bir şey yok
                    }

                    typeName = UnknownTypeName;
                    blankTypeRows++;
                }

                ImportTypePlan plan;
                if (!planByName.TryGetValue(typeName, out plan))
                {
                    plan = new ImportTypePlan { Name = typeName };

                    ProductType existingType;
                    if (typesByName.TryGetValue(typeName, out existingType))
                    {
                        plan.Existing = existingType;
                        plan.Name = existingType.Name;
                    }

                    planByName[typeName] = plan;
                    plans.Add(plan);
                }

                plan.Rows.Add(cells);
            }

            foreach (var plan in plans)
            {
                var currentProperties = plan.Existing != null
                    ? TypePropertyRepository.GetForType(plan.Existing.Id)
                    : new List<PropertyDefinition>();

                foreach (var column in columns)
                {
                    var col = column;
                    var alreadyInType = col.Property != null && currentProperties.Any(p => p.Id == col.Property.Id);
                    var hasValue = plan.Rows.Any(rw => Cell(rw, col.Index).Length > 0);

                    if (alreadyInType)
                    {
                        plan.WriteColumns.Add(col);
                    }
                    else if (hasValue)
                    {
                        plan.AttachColumns.Add(col);
                        plan.WriteColumns.Add(col);
                    }
                }
            }

            return plans;
        }

        /// <summary>Hiçbir şey yazmadan önce, olacakların özetini gösterir.</summary>
        private bool ConfirmImport(List<ImportTypePlan> plans, List<ImportColumn> columns,
            string serialHeader, string systemHeader, int blankTypeRows, string duplicateNote)
        {
            var text = new StringBuilder();
            text.AppendLine("Dosyadan " + plans.Sum(p => p.Rows.Count) + " ürün satırı okundu.");

            var newTypes = plans.Where(p => p.Existing == null).ToList();
            if (newTypes.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("Yeni oluşturulacak gruplar (" + newTypes.Count + "):");
                foreach (var plan in newTypes.Take(10))
                {
                    text.AppendLine("   • " + plan.Name + " — bilgiler: " +
                                    JoinLimited(plan.AttachColumns.Select(c => c.PropertyName), 8));
                }
                if (newTypes.Count > 10)
                {
                    text.AppendLine("   ... ve " + (newTypes.Count - 10) + " grup daha");
                }
            }

            var newProperties = columns.Where(c => c.Property == null).Select(c => c.PropertyName + " (" + c.NewDataType + ")").ToList();
            if (newProperties.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("Kütüphaneye yeni eklenecek bilgiler (" + newProperties.Count + "): " + JoinLimited(newProperties, 12));
            }

            var extended = plans.Where(p => p.Existing != null && p.AttachColumns.Count > 0).ToList();
            if (extended.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("Var olan gruplara yeni bilgi eklenecek:");
                foreach (var plan in extended.Take(10))
                {
                    text.AppendLine("   • " + plan.Name + ": " + JoinLimited(plan.AttachColumns.Select(c => c.PropertyName), 8));
                }
                if (extended.Count > 10)
                {
                    text.AppendLine("   ... ve " + (extended.Count - 10) + " grup daha");
                }
            }

            if (serialHeader != null)
            {
                var serialColumnItem = columns.FirstOrDefault(c => c.IsSerial);
                text.AppendLine();
                text.AppendLine("Seri numarası: \"" + serialHeader + "\" sütunu, \"" +
                                (serialColumnItem != null ? serialColumnItem.PropertyName : SeriNoHeader) +
                                "\" özelliğine yazılacak.");
            }

            if (!string.IsNullOrEmpty(duplicateNote))
            {
                text.AppendLine();
                text.AppendLine(duplicateNote);
            }

            if (systemHeader != null)
            {
                text.AppendLine();
                text.AppendLine("Sistem adı: \"" + systemHeader + "\" sütunu, \"" + SistemIsmiHeader +
                                "\" özelliğine yazılacak.");
            }

            if (blankTypeRows > 0)
            {
                text.AppendLine();
                text.AppendLine("Grup hücresi boş " + blankTypeRows + " satır, \"" + UnknownTypeName +
                                "\" grubuna eklenecek.");
            }

            text.AppendLine();
            text.AppendLine("Devam edilsin mi?");

            return MessageBox.Show(this, text.ToString(), "İçe aktarmayı onayla",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        /// <summary>
        /// Özetin onaylanmasından sonra asıl yazma işini yapar. Hepsi TEK bağlantı ve TEK işlem
        /// (transaction) içinde yapılır: binlerce satırda bile birkaç saniye sürer ve bir hata
        /// olursa hiçbir şey eklenmez (yarım kalmaz).
        /// </summary>
        private ImportResult ExecuteImport(List<ImportTypePlan> plans, List<ImportColumn> columns, int quantityColumn)
        {
            var result = new ImportResult();

            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                using (var connection = Database.OpenConnection())
                using (var transaction = connection.BeginTransaction())
                {
                    WriteImport(connection, plans, columns, quantityColumn, result);
                    transaction.Commit();
                }
            }
            catch (Exception ex)
            {
                // İşlem geri alındı: hiçbir şey eklenmedi.
                result = new ImportResult { Failure = ex.Message };
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }

            return result;
        }

        private void WriteImport(SQLiteConnection connection, List<ImportTypePlan> plans,
            List<ImportColumn> columns, int quantityColumn, ImportResult result)
        {
            using (var logCommand = CreateCommand(connection,
                "INSERT INTO ActionLogs (CreatedAt, UserName, TypeName, Description, ActionType) " +
                "VALUES (@createdAt, @userName, @typeName, @description, @actionType);",
                "@createdAt", "@userName", "@typeName", "@description", "@actionType"))
            using (var productCommand = CreateCommand(connection,
                "INSERT INTO Products (ProductTypeId, SortOrder, Quantity, CreatedAt) " +
                "VALUES (@typeId, @sortOrder, @quantity, @createdAt); SELECT last_insert_rowid();",
                "@typeId", "@sortOrder", "@quantity", "@createdAt"))
            using (var valueCommand = CreateCommand(connection,
                "INSERT INTO ProductValues (ProductId, PropertyId, TextValue) " +
                "VALUES (@productId, @propertyId, @textValue);",
                "@productId", "@propertyId", "@textValue"))
            {
                Action<string, string, string> addLog = (logType, logText, logAction) =>
                {
                    logCommand.Parameters["@createdAt"].Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    logCommand.Parameters["@userName"].Value = Environment.UserName;
                    logCommand.Parameters["@typeName"].Value = (object)logType ?? DBNull.Value;
                    logCommand.Parameters["@description"].Value = logText;
                    logCommand.Parameters["@actionType"].Value = logAction;
                    logCommand.ExecuteNonQuery();
                };

                // 1) Kütüphanede olmayan özellikleri oluştur.
                foreach (var column in columns.Where(c => c.Property == null).ToList())
                {
                    var newId = InsertAndGetId(connection,
                        "INSERT INTO PropertyDefinitions (Name, DataType, IsSerialNumber) " +
                        "VALUES (@name, @dataType, @isSerial); SELECT last_insert_rowid();",
                        new SQLiteParameter("@name", column.PropertyName),
                        new SQLiteParameter("@dataType", column.NewDataType),
                        new SQLiteParameter("@isSerial", column.IsSerial ? 1 : 0));

                    column.Property = new PropertyDefinition
                    {
                        Id = newId,
                        Name = column.PropertyName,
                        DataType = column.NewDataType,
                        IsSerialNumber = column.IsSerial
                    };

                    addLog(null, column.PropertyName + " (" + column.NewDataType + ")", "Özellik eklendi");
                    result.CreatedProperties.Add(column.PropertyName);
                }

                // 2) Her cins için: (yoksa) cinsi oluştur, özellikleri bağla, ürünleri ekle.
                foreach (var plan in plans)
                {
                    int typeId;
                    if (plan.Existing == null)
                    {
                        typeId = InsertAndGetId(connection,
                            "INSERT INTO ProductTypes (Name) VALUES (@name); SELECT last_insert_rowid();",
                            new SQLiteParameter("@name", plan.Name));
                        addLog(plan.Name, "", "Cins eklendi");
                        result.CreatedTypes.Add(plan.Name);
                    }
                    else
                    {
                        typeId = plan.Existing.Id;
                    }

                    foreach (var attach in plan.AttachColumns)
                    {
                        AttachPropertyToType(connection, typeId, attach.Property.Id);
                        addLog(plan.Name, attach.PropertyName, "Cinse özellik atandı");

                        if (plan.Existing != null)
                        {
                            result.AttachedToExistingTypes.Add(plan.Name + ": " + attach.PropertyName);
                        }
                    }

                    var nextSortOrder = Convert.ToInt32(ScalarQuery(connection,
                        "SELECT COALESCE(MAX(SortOrder), 0) + 1 FROM Products WHERE ProductTypeId = @typeId;",
                        new SQLiteParameter("@typeId", typeId)));

                    var addedForType = 0;

                    foreach (var cells in plan.Rows)
                    {
                        var quantity = 1;
                        if (quantityColumn >= 0)
                        {
                            int.TryParse(Cell(cells, quantityColumn), out quantity);
                        }
                        if (quantity < 1)
                        {
                            quantity = 1;
                        }

                        productCommand.Parameters["@typeId"].Value = typeId;
                        productCommand.Parameters["@sortOrder"].Value = nextSortOrder;
                        productCommand.Parameters["@quantity"].Value = quantity;
                        productCommand.Parameters["@createdAt"].Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        var productId = Convert.ToInt32(productCommand.ExecuteScalar());
                        nextSortOrder++;

                        foreach (var write in plan.WriteColumns)
                        {
                            var raw = Cell(cells, write.Index);
                            var value = raw;

                            if (write.DataType == DynamicFieldFactory.YesNoDataType)
                            {
                                bool invalid;
                                value = NormalizeYesNo(raw, out invalid);
                                if (invalid)
                                {
                                    var note = write.PropertyName + ": \"" + raw + "\"";
                                    if (!result.InvalidYesNo.Contains(note))
                                    {
                                        result.InvalidYesNo.Add(note);
                                    }
                                }
                            }

                            valueCommand.Parameters["@productId"].Value = productId;
                            valueCommand.Parameters["@propertyId"].Value = write.Property.Id;
                            valueCommand.Parameters["@textValue"].Value = value;
                            valueCommand.ExecuteNonQuery();

                        }

                        result.Added++;
                        addedForType++;
                    }

                    if (addedForType > 0)
                    {
                        addLog(plan.Name, addedForType + " satır", "İçe aktarıldı");
                    }
                }
            }
        }

        // ---------- Veritabanı yardımcıları (hepsi aynı bağlantıyı kullanır) ----------

        private static SQLiteCommand CreateCommand(SQLiteConnection connection, string sql, params string[] parameterNames)
        {
            var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var name in parameterNames)
            {
                command.Parameters.Add(new SQLiteParameter(name));
            }
            return command;
        }

        private static object ScalarQuery(SQLiteConnection connection, string sql, params SQLiteParameter[] parameters)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                foreach (var parameter in parameters)
                {
                    command.Parameters.Add(parameter);
                }
                return command.ExecuteScalar();
            }
        }

        private static int InsertAndGetId(SQLiteConnection connection, string sql, params SQLiteParameter[] parameters)
        {
            return Convert.ToInt32(ScalarQuery(connection, sql, parameters));
        }

        private static HashSet<string> LoadStringSet(SQLiteConnection connection, string sql, params SQLiteParameter[] parameters)
        {
            var set = new HashSet<string>();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                foreach (var parameter in parameters)
                {
                    command.Parameters.Add(parameter);
                }

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (!reader.IsDBNull(0))
                        {
                            set.Add(reader.GetString(0));
                        }
                    }
                }
            }

            return set;
        }

        /// <summary>Özelliği cinse (en sona) bağlar; zaten bağlıysa bir şey yapmaz.</summary>
        private static void AttachPropertyToType(SQLiteConnection connection, int typeId, int propertyId)
        {
            var exists = Convert.ToInt32(ScalarQuery(connection,
                "SELECT COUNT(*) FROM TypeProperties WHERE ProductTypeId = @typeId AND PropertyId = @propId;",
                new SQLiteParameter("@typeId", typeId),
                new SQLiteParameter("@propId", propertyId)));
            if (exists > 0)
            {
                return;
            }

            var nextOrder = Convert.ToInt32(ScalarQuery(connection,
                "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM TypeProperties WHERE ProductTypeId = @typeId;",
                new SQLiteParameter("@typeId", typeId)));

            ScalarQuery(connection,
                "INSERT INTO TypeProperties (ProductTypeId, PropertyId, SortOrder) VALUES (@typeId, @propId, @order);",
                new SQLiteParameter("@typeId", typeId),
                new SQLiteParameter("@propId", propertyId),
                new SQLiteParameter("@order", nextOrder));
        }

        // =====================================================================
        // YARDIMCILAR
        // =====================================================================

        /// <summary>Verilen sütun seçeneklerini bir listede gösterip birini seçtirir. İptalde -1 döner.</summary>
        private int PickColumn(string title, string question, List<KeyValuePair<int, string>> options, int preselect)
        {
            var window = new Window
            {
                Title = title,
                Width = 420,
                Height = 440,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ShowInTaskbar = false
            };

            var root = new DockPanel { Margin = new Thickness(12) };

            var questionText = new TextBlock
            {
                Text = question,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            DockPanel.SetDock(questionText, Dock.Top);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            DockPanel.SetDock(buttons, Dock.Bottom);

            var okButton = new Button
            {
                Content = "Tamam",
                Width = 80,
                Margin = new Thickness(0, 0, 6, 0),
                IsDefault = true,
                IsEnabled = false
            };
            var cancelButton = new Button { Content = "İptal", Width = 80, IsCancel = true };
            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);

            var list = new ListBox();
            foreach (var option in options)
            {
                list.Items.Add(new ListBoxItem { Content = option.Value, Tag = option.Key });
            }

            list.SelectionChanged += delegate { okButton.IsEnabled = list.SelectedItem != null; };
            list.MouseDoubleClick += delegate
            {
                if (list.SelectedItem != null)
                {
                    window.DialogResult = true;
                }
            };
            okButton.Click += delegate { window.DialogResult = true; };

            if (preselect >= 0 && preselect < list.Items.Count)
            {
                list.SelectedIndex = preselect;
            }

            root.Children.Add(questionText);
            root.Children.Add(buttons);
            root.Children.Add(list);
            window.Content = root;

            if (window.ShowDialog() != true)
            {
                return -1;
            }

            var chosen = list.SelectedItem as ListBoxItem;
            return chosen == null ? -1 : (int)chosen.Tag;
        }

        /// <summary>Seçenekler arasında, başlığı verilen ada uyan varsa onun sırasını (yoksa -1) döner.</summary>
        private static int FindOptionIndex(List<KeyValuePair<int, string>> options, string[] headers, string wanted)
        {
            for (var i = 0; i < options.Count; i++)
            {
                if (string.Equals(headers[options[i].Key], wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Listede görünen metin: başlık + ilk dolu hücreden bir örnek.</summary>
        private static string DescribeColumn(string header, List<string[]> rows, int column)
        {
            for (var r = 1; r < rows.Count; r++)
            {
                var sample = Cell(rows[r], column);
                if (sample.Length > 0)
                {
                    if (sample.Length > 25)
                    {
                        sample = sample.Substring(0, 25) + "...";
                    }
                    return header + "     (örnek: " + sample + ")";
                }
            }

            return header;
        }

        private static string Cell(string[] cells, int index)
        {
            return index >= 0 && index < cells.Length ? cells[index].Trim() : "";
        }

        private static bool ColumnHasValues(List<string[]> rows, int column)
        {
            for (var r = 1; r < rows.Count; r++)
            {
                if (Cell(rows[r], column).Length > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsYesNoValue(string value)
        {
            return string.Equals(value, "Evet", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "Hayır", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Yeni özelliğin veri tipi: sütundaki tüm dolu değerler Evet/Hayır ise o tip, değilse Metin.</summary>
        private static string DetectDataType(List<string[]> rows, int column)
        {
            var any = false;
            for (var r = 1; r < rows.Count; r++)
            {
                var value = Cell(rows[r], column);
                if (value.Length == 0)
                {
                    continue;
                }

                any = true;
                if (!IsYesNoValue(value))
                {
                    return TextDataType;
                }
            }

            return any ? DynamicFieldFactory.YesNoDataType : TextDataType;
        }

        /// <summary>Evet/Hayır özelliği için değeri düzenler. Boş = Hayır. Geçersizse boş döner ve invalid=true olur.</summary>
        private static string NormalizeYesNo(string raw, out bool invalid)
        {
            invalid = false;

            if (raw.Length == 0)
            {
                return "Hayır";
            }
            if (string.Equals(raw, "Evet", StringComparison.OrdinalIgnoreCase))
            {
                return "Evet";
            }
            if (string.Equals(raw, "Hayır", StringComparison.OrdinalIgnoreCase))
            {
                return "Hayır";
            }

            invalid = true;
            return "";
        }

        private static string JoinLimited(IEnumerable<string> items, int max)
        {
            var list = items.ToList();
            if (list.Count <= max)
            {
                return string.Join(", ", list);
            }

            return string.Join(", ", list.Take(max)) + " ... (+" + (list.Count - max) + " tane daha)";
        }

        // ---------- CSV OKUMA: kodlama ve ayraç otomatik tanınır ----------

        /// <summary>
        /// Dosyanın içine bakarak türünü anlar (uzantıya güvenmez): .xlsx/.xlsm ise Excel gibi,
        /// değilse CSV gibi okur. Tablonun ilk satırı başlık satırıdır. İptalde null döner.
        /// </summary>
        private List<string[]> ReadTableFile(string path)
        {
            _tableRowNumbers = new List<int>();

            var bytes = ReadAllBytesShared(path);

            // .xlsx / .xlsm bir zip dosyasıdır: "PK" ile başlar.
            if (bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04)
            {
                return ReadXlsx(bytes);
            }

            // Eski Excel biçimi (.xls).
            if (bytes.Length >= 4 && bytes[0] == 0xD0 && bytes[1] == 0xCF && bytes[2] == 0x11 && bytes[3] == 0xE0)
            {
                throw new InvalidOperationException(
                    "Bu dosya eski Excel biçiminde (.xls). Excel'de \"Farklı Kaydet\" ile \".xlsx\" ya da \"CSV\" olarak kaydedip onu seç.");
            }

            var text = DecodeCsvBytes(bytes);
            if (text.IndexOf('\0') >= 0)
            {
                throw new InvalidOperationException("Bu dosya CSV ya da Excel (.xlsx) dosyası gibi görünmüyor.");
            }

            var csvRowNumbers = new List<int>();
            var csvRows = ParseCsvText(text, DetectSeparator(text), csvRowNumbers);
            _tableRowNumbers = csvRowNumbers;
            return csvRows;
        }

        /// <summary>Dosya Excel'de açıkken de okunabilsin diye paylaşımlı okur.</summary>
        private static byte[] ReadAllBytesShared(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                return memory.ToArray();
            }
        }

        // ---------- EXCEL (.xlsx) OKUMA: ek kütüphane gerekmez, dosya içindeki XML okunur ----------

        /// <summary>Excel çalışma kitabından bir sayfayı satır satır okur (birden çok sayfa varsa sorar).</summary>
        private List<string[]> ReadXlsx(byte[] bytes)
        {
            using (var stream = new MemoryStream(bytes))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                var workbook = LoadXmlEntry(zip, "xl/workbook.xml");
                if (workbook == null)
                {
                    throw new InvalidOperationException("Bu dosya bir Excel çalışma kitabı değil.");
                }

                // İlişki numarası -> sayfa dosyasının yolu
                var targets = new Dictionary<string, string>();
                var relationships = LoadXmlEntry(zip, "xl/_rels/workbook.xml.rels");
                if (relationships != null)
                {
                    foreach (XmlNode node in relationships.SelectNodes("//*[local-name()='Relationship']"))
                    {
                        var element = (XmlElement)node;
                        targets[element.GetAttribute("Id")] = element.GetAttribute("Target");
                    }
                }

                var sheetNames = new List<string>();
                var sheetPaths = new List<string>();
                foreach (XmlNode node in workbook.SelectNodes("//*[local-name()='sheet']"))
                {
                    var element = (XmlElement)node;

                    var state = element.GetAttribute("state");
                    if (state == "hidden" || state == "veryHidden")
                    {
                        continue;
                    }

                    string target;
                    if (!targets.TryGetValue(GetRelationshipId(element), out target))
                    {
                        continue;
                    }

                    sheetNames.Add(element.GetAttribute("name"));
                    sheetPaths.Add(target.StartsWith("/") ? target.TrimStart('/') : "xl/" + target);
                }

                if (sheetNames.Count == 0)
                {
                    throw new InvalidOperationException("Excel dosyasında okunabilir sayfa bulunamadı.");
                }

                var chosen = 0;
                if (sheetNames.Count > 1)
                {
                    var options = new List<KeyValuePair<int, string>>();
                    for (var i = 0; i < sheetNames.Count; i++)
                    {
                        options.Add(new KeyValuePair<int, string>(i, sheetNames[i]));
                    }

                    chosen = PickColumn("Sayfa seç",
                        "Excel dosyasında birden çok sayfa var.\nHangi sayfa içe aktarılsın?",
                        options, 0);
                    if (chosen < 0)
                    {
                        return null;
                    }
                }

                var sharedStrings = new List<string>();
                var sharedDoc = LoadXmlEntry(zip, "xl/sharedStrings.xml");
                if (sharedDoc != null)
                {
                    foreach (XmlNode si in sharedDoc.SelectNodes("//*[local-name()='si']"))
                    {
                        sharedStrings.Add(ReadRichText(si));
                    }
                }

                var dateStyles = ReadDateStyles(zip);

                var sheet = LoadXmlEntry(zip, sheetPaths[chosen]);
                if (sheet == null)
                {
                    throw new InvalidOperationException("Excel sayfası okunamadı.");
                }

                var rows = new List<string[]>();
                var lastRowNumber = 0;
                foreach (XmlNode rowNode in sheet.SelectNodes("//*[local-name()='row']"))
                {
                    // Excel'deki gerçek satır numarası (boş satırlar atlansa da numara korunur).
                    var rowNumber = lastRowNumber + 1;
                    var rowElement = rowNode as XmlElement;
                    if (rowElement != null)
                    {
                        int parsedRowNumber;
                        if (int.TryParse(rowElement.GetAttribute("r"), out parsedRowNumber) && parsedRowNumber > 0)
                        {
                            rowNumber = parsedRowNumber;
                        }
                    }
                    lastRowNumber = rowNumber;

                    var cellsByColumn = new Dictionary<int, string>();
                    var maxColumn = -1;
                    var nextColumn = 0;

                    foreach (XmlNode cellNode in rowNode.ChildNodes)
                    {
                        if (cellNode.LocalName != "c")
                        {
                            continue;
                        }

                        var cell = (XmlElement)cellNode;

                        var column = nextColumn;
                        var reference = cell.GetAttribute("r");
                        if (reference.Length > 0)
                        {
                            column = ColumnIndexFromReference(reference);
                        }
                        nextColumn = column + 1;

                        var value = ReadXlsxCell(cell, sharedStrings, dateStyles);
                        if (value.Length > 0)
                        {
                            cellsByColumn[column] = value;
                            if (column > maxColumn)
                            {
                                maxColumn = column;
                            }
                        }
                    }

                    if (maxColumn < 0)
                    {
                        continue;   // tamamen boş satır
                    }

                    var line = new string[maxColumn + 1];
                    for (var c = 0; c <= maxColumn; c++)
                    {
                        string cellValue;
                        line[c] = cellsByColumn.TryGetValue(c, out cellValue) ? cellValue : "";
                    }
                    rows.Add(line);
                    _tableRowNumbers.Add(rowNumber);
                }

                return rows;
            }
        }

        private static XmlDocument LoadXmlEntry(ZipArchive zip, string entryName)
        {
            var entry = zip.GetEntry(entryName);
            if (entry == null)
            {
                return null;
            }

            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using (var entryStream = entry.Open())
            using (var reader = XmlReader.Create(entryStream, settings))
            {
                var document = new XmlDocument();
                document.Load(reader);
                return document;
            }
        }

        /// <summary>&lt;sheet r:id="rId1"&gt; içindeki r:id değerini bulur (ön ek ne olursa olsun).</summary>
        private static string GetRelationshipId(XmlElement element)
        {
            foreach (XmlAttribute attribute in element.Attributes)
            {
                if (attribute.LocalName == "id" && attribute.Prefix.Length > 0)
                {
                    return attribute.Value;
                }
            }

            return "";
        }

        /// <summary>Ortak metin (sharedStrings) ya da satır içi metin düğümünün yazısını birleştirir.</summary>
        private static string ReadRichText(XmlNode node)
        {
            var text = new StringBuilder();

            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.LocalName == "t")
                {
                    text.Append(child.InnerText);
                }
                else if (child.LocalName == "r")
                {
                    foreach (XmlNode part in child.ChildNodes)
                    {
                        if (part.LocalName == "t")
                        {
                            text.Append(part.InnerText);
                        }
                    }
                }
            }

            return text.ToString();
        }

        /// <summary>Hücre başvurusundaki sütun harflerini (A, B, ..., AB) 0'dan başlayan sıraya çevirir.</summary>
        private static int ColumnIndexFromReference(string reference)
        {
            var index = 0;

            foreach (var ch in reference)
            {
                if (ch >= 'A' && ch <= 'Z')
                {
                    index = index * 26 + (ch - 'A' + 1);
                }
                else if (ch >= 'a' && ch <= 'z')
                {
                    index = index * 26 + (ch - 'a' + 1);
                }
                else
                {
                    break;
                }
            }

            return Math.Max(index - 1, 0);
        }

        /// <summary>Hücre stilleri arasında hangilerinin tarih biçiminde olduğunu bulur (sıra = hücrenin s numarası).</summary>
        private static List<bool> ReadDateStyles(ZipArchive zip)
        {
            var result = new List<bool>();

            var styles = LoadXmlEntry(zip, "xl/styles.xml");
            if (styles == null)
            {
                return result;
            }

            var customFormats = new Dictionary<int, string>();
            foreach (XmlNode node in styles.SelectNodes("//*[local-name()='numFmt']"))
            {
                var element = (XmlElement)node;
                int formatId;
                if (int.TryParse(element.GetAttribute("numFmtId"), out formatId))
                {
                    customFormats[formatId] = element.GetAttribute("formatCode");
                }
            }

            var cellXfs = styles.SelectSingleNode("//*[local-name()='cellXfs']");
            if (cellXfs == null)
            {
                return result;
            }

            foreach (XmlNode xf in cellXfs.ChildNodes)
            {
                if (xf.LocalName != "xf")
                {
                    continue;
                }

                int numFmtId;
                int.TryParse(((XmlElement)xf).GetAttribute("numFmtId"), out numFmtId);

                string code;
                if (!customFormats.TryGetValue(numFmtId, out code))
                {
                    code = null;
                }

                result.Add(IsDateFormat(numFmtId, code));
            }

            return result;
        }

        private static bool IsDateFormat(int numFmtId, string formatCode)
        {
            if (formatCode == null)
            {
                // Excel'in hazır tarih/saat biçimleri
                return (numFmtId >= 14 && numFmtId <= 22) || (numFmtId >= 45 && numFmtId <= 47);
            }

            // Tırnak içi metinleri, [..] bölümlerini ve \x kaçışlarını at; kalanda d/m/y/h/s harfi varsa tarih.
            var stripped = Regex.Replace(formatCode, "\"[^\"]*\"|\\[[^\\]]*\\]|\\\\.", "");
            return stripped.IndexOfAny(new[] { 'd', 'D', 'm', 'M', 'y', 'Y', 'h', 'H', 's', 'S' }) >= 0;
        }

        /// <summary>Bir hücrenin ekranda görünecek metnini üretir.</summary>
        private static string ReadXlsxCell(XmlElement cell, List<string> sharedStrings, List<bool> dateStyles)
        {
            var type = cell.GetAttribute("t");

            XmlNode valueNode = null;
            XmlNode inlineNode = null;
            foreach (XmlNode child in cell.ChildNodes)
            {
                if (child.LocalName == "v")
                {
                    valueNode = child;
                }
                else if (child.LocalName == "is")
                {
                    inlineNode = child;
                }
            }

            if (type == "inlineStr")
            {
                return inlineNode == null ? "" : ReadRichText(inlineNode).Trim();
            }

            if (valueNode == null)
            {
                return "";
            }

            var raw = valueNode.InnerText;

            if (type == "s")
            {
                int index;
                if (int.TryParse(raw, out index) && index >= 0 && index < sharedStrings.Count)
                {
                    return sharedStrings[index].Trim();
                }
                return "";
            }

            if (type == "str" || type == "d")
            {
                return raw.Trim();
            }
            if (type == "b")
            {
                return raw == "1" ? "Evet" : "Hayır";
            }
            if (type == "e")
            {
                return "";
            }

            // Sayı (ya da tarih olarak biçimlenmiş sayı)
            double number;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            {
                return raw.Trim();
            }

            int styleIndex;
            if (int.TryParse(cell.GetAttribute("s"), out styleIndex) &&
                styleIndex >= 0 && styleIndex < dateStyles.Count && dateStyles[styleIndex])
            {
                return FormatExcelDate(number);
            }

            // Excel dosyasında düz rakam olarak yazılmışsa (IMEI, seri no...) aynen al
            var digitsOnly = raw.Length > 0;
            foreach (var ch in raw)
            {
                if (ch < '0' || ch > '9')
                {
                    digitsOnly = false;
                    break;
                }
            }
            if (digitsOnly)
            {
                return raw;
            }

            // 2,78E+15 gibi bilimsel gösterime düşmesin: tam sayıysa uzun rakam olarak yaz
            if (Math.Abs(number) < 1e18 && number == Math.Floor(number))
            {
                return ((long)number).ToString(CultureInfo.InvariantCulture);
            }

            return number.ToString("G15", CultureInfo.CurrentCulture);
        }

        private static string FormatExcelDate(double number)
        {
            try
            {
                var date = DateTime.FromOADate(number);

                if (number < 1)
                {
                    return date.ToString("HH:mm", CultureInfo.InvariantCulture);
                }
                if (number == Math.Floor(number))
                {
                    return date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
                }

                return date.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
            }
            catch (ArgumentException)
            {
                return number.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>UTF-8 (BOM'lu/BOM'suz), UTF-16 ve Excel'in Türkçe ANSI (Windows-1254) kaydını tanır.</summary>
        private static string DecodeCsvBytes(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            }

            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.GetEncoding(1254).GetString(bytes);
            }
        }

        /// <summary>İlk satırdaki (tırnak dışı) ; , ve sekme sayısına bakarak ayracı seçer.</summary>
        private static char DetectSeparator(string text)
        {
            var semicolons = 0;
            var commas = 0;
            var tabs = 0;
            var inQuotes = false;

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }
                if (inQuotes)
                {
                    continue;
                }
                if (c == '\n')
                {
                    break;
                }

                if (c == ';')
                {
                    semicolons++;
                }
                else if (c == ',')
                {
                    commas++;
                }
                else if (c == '\t')
                {
                    tabs++;
                }
            }

            if (semicolons > 0 && semicolons >= commas && semicolons >= tabs)
            {
                return ';';
            }
            if (tabs > 0 && tabs >= commas)
            {
                return '\t';
            }
            if (commas > 0)
            {
                return ',';
            }

            return ';';
        }

        /// <summary>Çift tırnak içinde ayraç ve satır sonu olabilen basit CSV okuyucu. Tamamen boş satırlar atılır.</summary>
        private static List<string[]> ParseCsvText(string text, char separator, List<int> rowNumbers = null)
        {
            var rows = new List<string[]>();
            var current = new List<string>();
            var field = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        field.Append(c);
                    }
                    continue;
                }

                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == separator)
                {
                    current.Add(field.ToString());
                    field.Clear();
                }
                else if (c == '\r')
                {
                    // yok say, \n satırı bitirecek
                }
                else if (c == '\n')
                {
                    current.Add(field.ToString());
                    field.Clear();
                    rows.Add(current.ToArray());
                    current = new List<string>();
                }
                else
                {
                    field.Append(c);
                }
            }

            if (field.Length > 0 || current.Count > 0)
            {
                current.Add(field.ToString());
                rows.Add(current.ToArray());
            }

            // Tamamen boş satırlar atılır; kalanların dosyadaki gerçek sıra numarası ayrıca döner.
            var kept = new List<string[]>();
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].Any(cell => cell.Trim().Length > 0))
                {
                    kept.Add(rows[i].Select(UnwrapExcelText).ToArray());
                    if (rowNumbers != null)
                    {
                        rowNumbers.Add(i + 1);
                    }
                }
            }

            return kept;
        }

        // =====================================================================
        // HURDAYA İÇE AKTAR (gerçek depoya hiç dokunmaz)
        // =====================================================================
        //
        // Excel/CSV dosyasındaki satırlar doğrudan HURDA tablolarına yazılır. Gerçek depoda
        // grup, özellik ya da ürün oluşturulmaz/değiştirilmez. Hurdadan geri getirirken grup ve
        // özellikler eksikse zaten otomatik oluşturulur. Tarih yazılmaz (boş kalır).
        // Aynı seri no'lu satırların hepsi eklenir (içe aktarmada seri no tekilliği aranmaz).

        private class ScrapImportRow
        {
            public string TypeName;
            public int Rank;
            public int Quantity;
            public string[] Cells;
        }

        private void ImportScrapMenu_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Hurdaya İçe Aktar",
                Filter = "Excel veya CSV (*.xlsx;*.xlsm;*.csv)|*.xlsx;*.xlsm;*.csv|Tüm dosyalar (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            List<string[]> rows;
            try
            {
                rows = ReadTableFile(dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Dosya okunamadı: " + ex.Message, "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (rows == null)
            {
                return;   // kullanıcı sayfa seçiminden vazgeçti
            }

            if (rows.Count < 1)
            {
                MessageBox.Show("Dosyada okunacak satır bulunamadı.",
                    "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Önce kısa, örnekli bilgi ekranı.
            if (!ShowImportInfo("Hurdaya İçe Aktar", true))
            {
                return;
            }

            // Kullanıcı satırları ve sütunları kendisi ayarlar.
            int cinsColumn, serialColumn, systemColumn;
            rows = ShowImportSettings(rows, "Hurdaya İçe Aktar",
                out cinsColumn, out serialColumn, out systemColumn);
            if (rows == null)
            {
                return;
            }

            var headers = rows[0].Select(h => h.Trim()).ToArray();

            var ignoredColumns = new List<string>();
            int quantityColumn;
            var columns = BuildImportColumns(rows, headers, cinsColumn, serialColumn, systemColumn,
                out quantityColumn, ignoredColumns);

            var duplicateNote = BuildDuplicateSerialNote(rows, serialColumn);

            // Satırları cinslerine göre sırala; her cinsin kendi sırası dosyadaki sırası olur.
            var items = new List<ScrapImportRow>();
            var typeNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var rankByType = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var blankTypeRows = 0;

            for (var r = 1; r < rows.Count; r++)
            {
                var cells = rows[r];
                var typeName = Cell(cells, cinsColumn);

                if (typeName.Length == 0)
                {
                    var rowHasData = columns.Any(c => Cell(cells, c.Index).Length > 0);
                    if (!rowHasData)
                    {
                        continue;   // tamamen boş satır
                    }

                    typeName = UnknownTypeName;
                    blankTypeRows++;
                }

                string canonicalName;
                if (!typeNames.TryGetValue(typeName, out canonicalName))
                {
                    canonicalName = typeName;
                    typeNames[typeName] = canonicalName;
                }

                var quantity = 1;
                if (quantityColumn >= 0)
                {
                    int.TryParse(Cell(cells, quantityColumn), out quantity);
                }
                if (quantity < 1)
                {
                    quantity = 1;
                }

                int rank;
                rankByType.TryGetValue(canonicalName, out rank);
                rank++;
                rankByType[canonicalName] = rank;

                items.Add(new ScrapImportRow
                {
                    TypeName = canonicalName,
                    Rank = rank,
                    Quantity = quantity,
                    Cells = cells
                });
            }

            if (items.Count == 0)
            {
                MessageBox.Show("Hurdaya eklenecek ürün satırı bulunamadı.",
                    "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Onay: özet göster.
            var summary = new StringBuilder();
            summary.AppendLine(items.Count + " satır HURDAYA eklenecek:");
            summary.AppendLine();
            foreach (var group in items.GroupBy(i => i.TypeName))
            {
                summary.AppendLine("   • " + group.Key + ": " + group.Count() + " satır");
            }
            if (!string.IsNullOrEmpty(duplicateNote))
            {
                summary.AppendLine();
                summary.AppendLine(duplicateNote);
            }
            summary.AppendLine();
            summary.AppendLine("Gerçek depoya hiçbir şey eklenmez ve değişmez.");
            summary.AppendLine("Devam edilsin mi?");

            if (MessageBox.Show(this, summary.ToString().TrimEnd(), "Depo Durumu",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            // Her cinste hangi sütunlar kullanılacak: o cinsin satırlarında en az bir dolu hücresi olanlar.
            var columnsByType = new Dictionary<string, List<ImportColumn>>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in items.GroupBy(i => i.TypeName))
            {
                var groupItems = group.ToList();
                columnsByType[group.Key] = columns
                    .Where(c => groupItems.Any(i => Cell(i.Cells, c.Index).Length > 0))
                    .ToList();
            }

            var added = 0;
            var invalidYesNo = new List<string>();
            var countByType = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string failure = null;

            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                using (var connection = Database.OpenConnection())
                using (var transaction = connection.BeginTransaction())
                using (var scrapCommand = CreateCommand(connection,
                    "INSERT INTO ScrapProducts (TypeId, TypeName, OriginalSortOrder, Quantity, ScrappedAt) " +
                    "VALUES (NULL, @typeName, @order, @quantity, @scrappedAt); SELECT last_insert_rowid();",
                    "@typeName", "@order", "@quantity", "@scrappedAt"))
                using (var valueCommand = CreateCommand(connection,
                    "INSERT INTO ScrapProductValues " +
                    "(ScrapProductId, PropertyId, PropertyName, DataType, IsSerialNumber, TextValue) " +
                    "VALUES (@scrapId, @propertyId, @propertyName, @dataType, @isSerial, @value);",
                    "@scrapId", "@propertyId", "@propertyName", "@dataType", "@isSerial", "@value"))
                {
                    foreach (var item in items)
                    {
                        scrapCommand.Parameters["@typeName"].Value = item.TypeName;
                        scrapCommand.Parameters["@order"].Value = item.Rank;
                        scrapCommand.Parameters["@quantity"].Value = item.Quantity;
                        scrapCommand.Parameters["@scrappedAt"].Value = "";
                        var scrapId = Convert.ToInt32(scrapCommand.ExecuteScalar());

                        foreach (var column in columnsByType[item.TypeName])
                        {
                            var raw = Cell(item.Cells, column.Index);
                            var value = raw;

                            if (column.DataType == DynamicFieldFactory.YesNoDataType)
                            {
                                bool invalid;
                                value = NormalizeYesNo(raw, out invalid);
                                if (invalid)
                                {
                                    var note = column.PropertyName + ": \"" + raw + "\"";
                                    if (!invalidYesNo.Contains(note))
                                    {
                                        invalidYesNo.Add(note);
                                    }
                                }
                            }

                            valueCommand.Parameters["@scrapId"].Value = scrapId;
                            valueCommand.Parameters["@propertyId"].Value =
                                column.Property != null ? (object)column.Property.Id : DBNull.Value;
                            valueCommand.Parameters["@propertyName"].Value = column.PropertyName;
                            valueCommand.Parameters["@dataType"].Value = column.DataType;
                            valueCommand.Parameters["@isSerial"].Value = column.IsSerialNumberProperty ? 1 : 0;
                            valueCommand.Parameters["@value"].Value = value;
                            valueCommand.ExecuteNonQuery();
                        }

                        added++;
                        int typeCount;
                        countByType.TryGetValue(item.TypeName, out typeCount);
                        countByType[item.TypeName] = typeCount + 1;
                    }

                    transaction.Commit();
                }
            }
            catch (Exception ex)
            {
                failure = ex.Message;   // işlem geri alındı: hurdaya hiçbir şey eklenmedi
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }

            if (failure != null)
            {
                MessageBox.Show(this,
                    "Hurdaya içe aktarma tamamlanamadı; hiçbir değişiklik yapılmadı.\n\nNeden: " + failure,
                    "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            foreach (var pair in countByType)
            {
                LogRepository.Add(pair.Key, pair.Value + " satır", "Hurdaya içe aktarıldı");
            }

            var message = new StringBuilder();
            message.AppendLine(added + " ürün hurdaya içe aktarıldı.");
            if (blankTypeRows > 0)
            {
                message.AppendLine();
                message.AppendLine("Grup hücresi boş olan satırlar \"" + UnknownTypeName + "\" grubuna eklendi.");
            }
            if (invalidYesNo.Count > 0)
            {
                message.AppendLine();
                message.AppendLine("Evet/Hayır özelliğine uymayan değerler boş bırakıldı: " + JoinLimited(invalidYesNo, 10));
            }
            if (ignoredColumns.Count > 0)
            {
                message.AppendLine();
                message.AppendLine("Yok sayılan sütunlar: " + JoinLimited(ignoredColumns, 10));
            }
            MessageBox.Show(this, message.ToString().TrimEnd(), "Depo Durumu",
                MessageBoxButton.OK, MessageBoxImage.Information);

            if (TypePage.Visibility == Visibility.Visible && _currentType != null)
            {
                LoadProductGrid(_currentType);
            }
            else
            {
                ShowHome();
            }
        }

        // =====================================================================
        // AYNI SERİ NO UYARISI (içe aktarmada engel değil, sadece bilgi)
        // =====================================================================

        /// <summary>
        /// İçe aktarılacak satırlarda (ya da depoda/hurdada zaten) aynı seri no birden fazla
        /// geçiyorsa bir bilgi cümlesi döner; yoksa null. İçe aktarmada bunlar engellenmez.
        /// </summary>
        private string BuildDuplicateSerialNote(List<string[]> rows, int serialColumn)
        {
            if (serialColumn < 0)
            {
                return null;
            }

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var order = new List<string>();

            for (var r = 1; r < rows.Count; r++)
            {
                var value = Cell(rows[r], serialColumn);
                if (value.Length == 0)
                {
                    continue;
                }

                int count;
                if (!counts.TryGetValue(value, out count))
                {
                    order.Add(value);
                }
                counts[value] = count + 1;
            }

            HashSet<string> existing;
            using (var connection = Database.OpenConnection())
            {
                existing = LoadStringSet(connection,
                    "SELECT pv.TextValue FROM ProductValues pv " +
                    "JOIN PropertyDefinitions pd ON pd.Id = pv.PropertyId " +
                    "WHERE pd.IsSerialNumber = 1 AND pv.TextValue IS NOT NULL;");
                existing.UnionWith(LoadStringSet(connection,
                    "SELECT TextValue FROM ScrapProductValues WHERE IsSerialNumber = 1 AND TextValue IS NOT NULL;"));
            }

            var duplicates = new List<string>();
            foreach (var value in order)
            {
                var inDatabase = existing.Contains(value);
                if (counts[value] > 1 || inDatabase)
                {
                    duplicates.Add(value + " (dosyada " + counts[value] + " kez" +
                                   (inDatabase ? ", depoda/hurdada da var" : "") + ")");
                }
            }

            if (duplicates.Count == 0)
            {
                return null;
            }

            return "Aynı seri no'ya sahip ürünler var: " + JoinLimited(duplicates, 8) +
                   ". Hepsi olduğu gibi eklenecek.";
        }

        // =====================================================================
        // İÇE AKTARMA AYARLARI PENCERESİ
        // =====================================================================
        //
        // Kullanıcı dosyanın ilk satırlarını (Excel'deki satır numaralarıyla) görür; sütun
        // başlıklarının hangi satırda, ürünlerin hangi satırdan başladığını kendisi yazar ve
        // her sütunun ne olduğunu kendisi seçer. Program hiçbir şeyi tahmin etmez; sadece
        // bilinen başlıkları (Grup, Seri No, Adet...) hazır seçili getirir, kullanıcı değiştirebilir.
        // Sonuç: ilk satırı sütun adları, kalan satırları ürünler olan, sütun sırası korunmuş bir tablo.

        private const string RoleCins = "Grup (ürünün türü) → telsiz, kişisel bilgisayar, telefon vs. yazan sütunu seç";
        private const string RoleSerial = "Seri No (ürünün seri numarası) → H4V9C2T7QZ gibi her ürüne özel numara yazan sütunu seç";
        private const string RoleSystem = "Sistem Adı (ETMYS Adı) → LENOVO BİLGİSAYAR gibi sistemdeki kayıtlı ad yazan sütunu seç";
        private const string RoleQuantity = "Adet (kaç tane olduğu) → 1, 2, 10 gibi kaç tane olduğu yazan sütunu seç";
        private const string RoleOther = "Diğer özellik (marka, model...) → LENOVO gibi marka, 160 GB gibi hafıza yazan sütunu seç";
        // Listede görünmez; seçim yapılmamış sütun demektir (aktarılmaz). Sütun aktarmama konusu henüz karara bağlanmadı.
        private const string RoleSkip = "(seçilmedi)";
        private const string EmptySample = "(boş)";

        // Dosyadaki satırların Excel'deki gerçek numaraları (boş satırlar atlandığı için ayrıca tutulur).
        private List<int> _tableRowNumbers = new List<int>();

        private class SettingsColumn
        {
            public int Index;
            public string Letter;
            public TextBlock HeaderInfo;
            public TextBlock Samples;
            public TextBox NameBox;
            public ComboBox RoleBox;
            public bool NameEdited;
            public bool RoleEdited;
            public bool HasData;
        }

        private List<string[]> ShowImportSettings(List<string[]> rows, string title,
            out int cinsColumn, out int serialColumn, out int systemColumn)
        {
            cinsColumn = -1;
            serialColumn = -1;
            systemColumn = -1;

            var numbers = new List<int>(_tableRowNumbers);
            if (numbers.Count != rows.Count)
            {
                numbers = new List<int>();
                for (var i = 0; i < rows.Count; i++)
                {
                    numbers.Add(i + 1);
                }
            }

            var width = 1;
            foreach (var row in rows)
            {
                if (row.Length > width)
                {
                    width = row.Length;
                }
            }

            var window = new Window
            {
                Title = title + " — İçe Aktarma Ayarları",
                Width = 1150,
                Height = 780,
                MinWidth = 720,
                MinHeight = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ShowInTaskbar = false,
                Background = Brushes.White,
                FontSize = 11
            };

            var root = new DockPanel { Margin = new Thickness(14) };

            // ---------- 1. adım açıklaması (yanıp söner) ----------
            var step1 = MakeStepBanner("1) Satırları belirle:",
                "Üstte İL, CİNS, SERİ NO gibi başlıkların yazdığı satırın numarasını ve ürünlerin başladığı satırın numarasını yaz.",
                "Örnek: 1. satırda İL | CİNS | SERİ NO | SİSTEM ADI | MARKA | NOT yazıyorsa başlık satırı 1, " +
                "ürünler 2. satırdan başlar. Başlık yoksa \"Başlık yok\"u işaretle.");
            DockPanel.SetDock(step1, Dock.Top);

            // ---------- Butonlar ----------
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            DockPanel.SetDock(buttons, Dock.Bottom);

            var okButton = new Button
            {
                Content = "Devam",
                Width = 100,
                Padding = new Thickness(0, 5, 0, 5),
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x6F, 0xD4))
            };
            var cancelButton = new Button { Content = "İptal", Width = 90, Padding = new Thickness(0, 5, 0, 5), IsCancel = true };
            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);

            // ---------- Önizleme ----------
            var previewCount = Math.Min(rows.Count, 40);
            var shownColumns = Math.Min(width, 40);

            var headerBrush = new SolidColorBrush(Color.FromRgb(0xCF, 0xE8, 0xFF));
            var dataBrush = new SolidColorBrush(Color.FromRgb(0xDF, 0xF5, 0xDF));
            var otherBrush = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));

            var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 4) };
            legend.Children.Add(MakeLegendItem(headerBrush, "Başlık satırı (İL, CİNS gibi yazılar)"));
            legend.Children.Add(MakeLegendItem(dataBrush, "Ürün satırları"));
            legend.Children.Add(MakeLegendItem(otherBrush, "Alınmayacak satırlar"));
            if (rows.Count > previewCount)
            {
                legend.Children.Add(new TextBlock
                {
                    Text = "(İlk " + previewCount + " satır gösteriliyor)",
                    Foreground = Brushes.DimGray,
                    Margin = new Thickness(10, 0, 0, 0)
                });
            }
            DockPanel.SetDock(legend, Dock.Top);

            var previewGrid = new Grid();
            previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            for (var c = 0; c < shownColumns; c++)
            {
                previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            }
            for (var r = 0; r <= previewCount; r++)
            {
                previewGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            var cornerCell = MakePreviewCell("Satır", true);
            cornerCell.Background = Brushes.WhiteSmoke;
            Grid.SetRow(cornerCell, 0);
            Grid.SetColumn(cornerCell, 0);
            previewGrid.Children.Add(cornerCell);

            for (var c = 0; c < shownColumns; c++)
            {
                var letterCell = MakePreviewCell(ColumnLetter(c), true);
                letterCell.Background = Brushes.WhiteSmoke;
                Grid.SetRow(letterCell, 0);
                Grid.SetColumn(letterCell, c + 1);
                previewGrid.Children.Add(letterCell);
            }

            var previewRowCells = new List<Border[]>();
            for (var i = 0; i < previewCount; i++)
            {
                var cellsOfRow = new Border[shownColumns + 1];

                var numberCell = MakePreviewCell(numbers[i].ToString(), true);
                Grid.SetRow(numberCell, i + 1);
                Grid.SetColumn(numberCell, 0);
                previewGrid.Children.Add(numberCell);
                cellsOfRow[0] = numberCell;

                for (var c = 0; c < shownColumns; c++)
                {
                    var cell = MakePreviewCell(Cell(rows[i], c), false);
                    Grid.SetRow(cell, i + 1);
                    Grid.SetColumn(cell, c + 1);
                    previewGrid.Children.Add(cell);
                    cellsOfRow[c + 1] = cell;
                }

                previewRowCells.Add(cellsOfRow);
            }

            var previewScroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Height = 190,
                Content = previewGrid,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1)
            };
            DockPanel.SetDock(previewScroll, Dock.Top);

            // ---------- Satır ayarları ----------
            Func<string, TextBlock> makeLabel = text => new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };

            var headerBox = new TextBox { Width = 60, Text = numbers[0].ToString(), VerticalContentAlignment = VerticalAlignment.Center };
            var noHeaderCheck = new CheckBox
            {
                Content = "Başlık yok (dosyada İL, CİNS gibi yazılar yok)",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 0, 0)
            };
            var startBox = new TextBox
            {
                Width = 60,
                Text = (numbers.Count > 1 ? numbers[1] : numbers[0] + 1).ToString(),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            var endBox = new TextBox { Width = 60, VerticalContentAlignment = VerticalAlignment.Center };

            var line1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            line1.Children.Add(MakeRedBlinkLabel("Başlıklar şu satırda:"));
            line1.Children.Add(headerBox);
            line1.Children.Add(new TextBlock
            {
                Text = "(örnek: İL, CİNS, SERİ NO, MARKA vs.)",
                Foreground = Brushes.DimGray,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            });
            line1.Children.Add(noHeaderCheck);

            var line2 = new StackPanel { Orientation = Orientation.Horizontal };
            line2.Children.Add(MakeRedBlinkLabel("Ürünler şu satırdan başlıyor:"));
            line2.Children.Add(startBox);
            line2.Children.Add(new TextBlock
            {
                Text = "(örnek: TELSİZ, H4V9C2T7QZ, LENOVO vs.)",
                Foreground = Brushes.DimGray,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            });
            var untilLabel = makeLabel("şu satıra kadar:");
            untilLabel.Margin = new Thickness(18, 0, 6, 0);
            line2.Children.Add(untilLabel);
            line2.Children.Add(endBox);
            line2.Children.Add(new TextBlock
            {
                Text = "(boş bırakırsan dosyanın sonuna kadar)",
                Foreground = Brushes.DimGray,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            });

            var settingsInner = new StackPanel();
            settingsInner.Children.Add(line1);
            settingsInner.Children.Add(line2);
            var settingsPanel = new Border
            {
                Child = settingsInner,
                Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF7)),
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 6, 0, 10)
            };
            DockPanel.SetDock(settingsPanel, Dock.Top);

            // ---------- 2. adım açıklaması (yanıp söner) ----------
            var step2 = MakeStepBanner("2) Sütunları tanıt:",
                "Aşağıdaki listede her sütunda ne yazdığını seç.",
                "Örnek: altında LMXLKD9 gibi numaralar varsa \"Seri No\", KİŞİSEL BİLGİSAYAR gibi türler varsa \"Grup\" seç.");
            DockPanel.SetDock(step2, Dock.Top);

            // ---------- Grup sütununun önemini anlatan uyarı ----------
            var groupNoteText = new TextBlock { TextWrapping = TextWrapping.Wrap };
            groupNoteText.Inlines.Add(new System.Windows.Documents.Run("ÖNEMLİ: ") { FontWeight = FontWeights.Bold });
            var groupBlinkColor = Color.FromRgb(0x8B, 0x00, 0x00);   // koyu kırmızı zemin
            var groupBlinkBack = new SolidColorBrush(Color.FromArgb(0, groupBlinkColor.R, groupBlinkColor.G, groupBlinkColor.B));
            var groupBlinkFore = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C));
            groupBlinkBack.BeginAnimation(SolidColorBrush.ColorProperty, new System.Windows.Media.Animation.ColorAnimation
            {
                From = Color.FromArgb(0, groupBlinkColor.R, groupBlinkColor.G, groupBlinkColor.B),
                To = groupBlinkColor,
                Duration = new Duration(TimeSpan.FromMilliseconds(700)),
                AutoReverse = true,
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                EasingFunction = new System.Windows.Media.Animation.SineEase()
            });
            groupBlinkFore.BeginAnimation(SolidColorBrush.ColorProperty, new System.Windows.Media.Animation.ColorAnimation
            {
                From = Color.FromRgb(0x1C, 0x1C, 0x1C),
                To = Colors.White,
                Duration = new Duration(TimeSpan.FromMilliseconds(700)),
                AutoReverse = true,
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                EasingFunction = new System.Windows.Media.Animation.SineEase()
            });
            groupNoteText.Inlines.Add(new System.Windows.Documents.Run("\"Grup (ürünün türü)\"")
            {
                FontWeight = FontWeights.Bold,
                Background = groupBlinkBack,
                Foreground = groupBlinkFore
            });
            groupNoteText.Inlines.Add(new System.Windows.Documents.Run(
                " sütunu çok önemlidir. Program ürünleri bu sütuna göre gruplar (telsiz, bilgisayar, yazıcı vs.). " +
                "Ürünün türünün yazdığı sütunu mutlaka seç. Dosyada böyle bir sütun yoksa Devam'a basınca " +
                "tüm ürünler için bir grup adı yazman istenecek."));
            var groupNote = new Border
            {
                Child = groupNoteText,
                Background = new SolidColorBrush(Color.FromRgb(0xFD, 0xEC, 0xEA)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x8B, 0x00, 0x00)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 4, 0, 4)
            };
            DockPanel.SetDock(groupNote, Dock.Top);

            // ---------- Sütun listesi ----------
            var columnsTitle = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            DockPanel.SetDock(columnsTitle, Dock.Top);

            var helpButton = new Button
            {
                Content = "💡 Hangi sütuna ne seçmeliyim?",
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(0, 0, 8, 0)
            };
            DockPanel.SetDock(helpButton, Dock.Right);
            columnsTitle.Children.Add(helpButton);

            columnsTitle.Children.Add(new TextBlock
            {
                Text = "Her sütunda ne yazdığını seç",
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            });

            Func<Grid> newRowGrid = () =>
            {
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(500) });
                return g;
            };

            var columnsPanel = new StackPanel();

            var titleRow = newRowGrid();
            var titleTexts = new[]
            {
                "Sütun",
                "Bu sütunda yazanlar (ilk 3 ürün)",
                "Program bu bilgiye ne ad versin?",
                "Bu sütunda ne yazıyor?"
            };
            for (var t = 0; t < titleTexts.Length; t++)
            {
                var titleCell = new TextBlock
                {
                    Text = titleTexts[t],
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(6, 4, 4, 4)
                };
                Grid.SetColumn(titleCell, t);
                titleRow.Children.Add(titleCell);
            }
            titleRow.Background = Brushes.WhiteSmoke;
            columnsPanel.Children.Add(titleRow);

            var columnsScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = columnsPanel,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1)
            };

            // ---------- Durum ve yardımcı işlevler ----------
            var updating = false;
            var startEdited = false;
            var columnItems = new List<SettingsColumn>();

            Func<TextBox, int> readInt = box =>
            {
                int value;
                return int.TryParse(box.Text.Trim(), out value) && value > 0 ? value : -1;
            };

            // Sütun başlıkları satırı: 0 = yok, -1 = geçersiz yazı.
            Func<int> headerRowNumber = () => noHeaderCheck.IsChecked == true ? 0 : readInt(headerBox);
            Func<int> startRowNumber = () => readInt(startBox);
            Func<int> endRowNumber = () => endBox.Text.Trim().Length == 0 ? int.MaxValue : readInt(endBox);

            Func<int, bool> isDataRow = n =>
            {
                var start = startRowNumber();
                return start > 0 && n >= start && n <= endRowNumber() && n != headerRowNumber();
            };

            // Başlık satırından sonraki ilk dolu satır (ürünlerin başlangıcı için varsayılan).
            Func<int> nextRowAfterHeader = () =>
            {
                var h = headerRowNumber();
                if (h <= 0)
                {
                    return numbers[0];
                }
                foreach (var n in numbers)
                {
                    if (n > h)
                    {
                        return n;
                    }
                }
                return h + 1;
            };

            Action refreshPreview = () =>
            {
                var h = headerRowNumber();
                for (var i = 0; i < previewRowCells.Count; i++)
                {
                    var n = numbers[i];
                    Brush back;
                    if (n == h)
                    {
                        back = headerBrush;
                    }
                    else if (isDataRow(n))
                    {
                        back = dataBrush;
                    }
                    else
                    {
                        back = otherBrush;
                    }

                    foreach (var cell in previewRowCells[i])
                    {
                        cell.Background = back;
                    }
                }
            };

            Action applyDefaults = () =>
            {
                var previous = updating;
                updating = true;

                string[] headerCells = null;
                var h = headerRowNumber();
                if (h > 0)
                {
                    var headerIndex = numbers.IndexOf(h);
                    if (headerIndex >= 0)
                    {
                        headerCells = rows[headerIndex];
                    }
                }

                foreach (var col in columnItems)
                {
                    var headerText = headerCells != null && col.Index < headerCells.Length
                        ? headerCells[col.Index].Trim()
                        : "";

                    col.HeaderInfo.Text = headerText.Length > 0 ? "(" + headerText + ")" : "";

                    if (!col.NameEdited)
                    {
                        col.NameBox.Text = headerText;
                    }
                    if (!col.RoleEdited)
                    {
                        col.RoleBox.SelectedItem = DefaultRoleFor(headerText);
                    }

                    var sample = SampleText(rows, numbers, col.Index, isDataRow);
                    col.Samples.Text = sample;
                    col.HasData = sample != EmptySample;
                }

                updating = previous;
            };

            // ---------- Sütun satırlarını oluştur (dolu olan her sütun için bir satır) ----------
            var roleNames = new[] { RoleCins, RoleSerial, RoleSystem, RoleQuantity, RoleOther };

            for (var c = 0; c < width; c++)
            {
                var any = false;
                foreach (var row in rows)
                {
                    if (Cell(row, c).Length > 0)
                    {
                        any = true;
                        break;
                    }
                }
                if (!any)
                {
                    continue;
                }

                var item = new SettingsColumn { Index = c, Letter = ColumnLetter(c) };

                item.HeaderInfo = new TextBlock
                {
                    FontSize = 9,
                    Foreground = Brushes.Gray,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                item.Samples = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brushes.DimGray,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 6, 0)
                };
                item.NameBox = new TextBox { Margin = new Thickness(2), Padding = new Thickness(2) };
                item.RoleBox = new ComboBox { Margin = new Thickness(2) };
                foreach (var roleName in roleNames)
                {
                    item.RoleBox.Items.Add(roleName);
                }

                item.NameBox.TextChanged += delegate
                {
                    if (!updating)
                    {
                        item.NameEdited = true;
                    }
                };

                item.RoleBox.SelectionChanged += delegate
                {
                    if (updating)
                    {
                        return;
                    }

                    item.RoleEdited = true;

                    var selected = item.RoleBox.SelectedItem as string;
                    if (selected == RoleOther && item.NameBox.Text.Trim().Length == 0)
                    {
                        updating = true;
                        item.NameBox.Text = "Sütun " + item.Letter;
                        updating = false;
                    }
                };

                var rowGrid = newRowGrid();

                var letterPanel = new StackPanel { Margin = new Thickness(6, 3, 4, 3) };
                letterPanel.Children.Add(new TextBlock { Text = "Sütun " + item.Letter, FontWeight = FontWeights.Bold });
                letterPanel.Children.Add(item.HeaderInfo);
                Grid.SetColumn(letterPanel, 0);
                rowGrid.Children.Add(letterPanel);

                Grid.SetColumn(item.Samples, 1);
                rowGrid.Children.Add(item.Samples);
                Grid.SetColumn(item.NameBox, 2);
                rowGrid.Children.Add(item.NameBox);
                Grid.SetColumn(item.RoleBox, 3);
                rowGrid.Children.Add(item.RoleBox);

                var separator = new Border
                {
                    BorderBrush = Brushes.LightGray,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Child = rowGrid
                };
                columnsPanel.Children.Add(separator);
                columnItems.Add(item);
            }

            // ---------- Olaylar ----------
            headerBox.TextChanged += delegate
            {
                if (updating)
                {
                    return;
                }

                if (!startEdited)
                {
                    updating = true;
                    startBox.Text = nextRowAfterHeader().ToString();
                    updating = false;
                }

                refreshPreview();
                applyDefaults();
            };

            noHeaderCheck.Click += delegate
            {
                headerBox.IsEnabled = noHeaderCheck.IsChecked != true;

                if (!startEdited)
                {
                    updating = true;
                    startBox.Text = nextRowAfterHeader().ToString();
                    updating = false;
                }

                refreshPreview();
                applyDefaults();
            };

            startBox.TextChanged += delegate
            {
                if (updating)
                {
                    return;
                }

                startEdited = true;
                refreshPreview();
                applyDefaults();
            };

            endBox.TextChanged += delegate
            {
                if (updating)
                {
                    return;
                }

                refreshPreview();
                applyDefaults();
            };

            helpButton.Click += delegate
            {
                ShowColumnHelp(window);
            };

            // ---------- Devam: kontrol et ve sonucu hazırla ----------
            List<string[]> normalizedResult = null;
            var chosenCins = -1;
            var chosenSerial = -1;
            var chosenSystem = -1;

            Action<string> warn = text =>
                MessageBox.Show(window, text, "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Warning);

            okButton.Click += delegate
            {
                var headerNumber = headerRowNumber();
                if (headerNumber < 0)
                {
                    warn("Başlıkların yazdığı satırın numarasına bir sayı yaz.\n" +
                         "Dosyada başlık yoksa \"Başlık yok\" kutusunu işaretle.");
                    return;
                }

                var start = startRowNumber();
                if (start < 1)
                {
                    warn("Ürünlerin başladığı satırın numarasına bir sayı yaz.");
                    return;
                }

                var end = endRowNumber();
                if (end < start)
                {
                    warn("\"Şu satıra kadar\" kutusu ya boş olmalı ya da başlangıç satırından küçük olmamalı.");
                    return;
                }

                var dataRows = new List<string[]>();
                for (var i = 0; i < rows.Count; i++)
                {
                    if (isDataRow(numbers[i]))
                    {
                        dataRows.Add(rows[i]);
                    }
                }

                if (dataRows.Count == 0)
                {
                    warn("Belirttiğin satırlarda ürün bulunamadı.\n" +
                         "Ürünlerin başladığı satır numarasını kontrol et.");
                    return;
                }

                var roles = new string[width];
                var names = new string[width];
                for (var c2 = 0; c2 < width; c2++)
                {
                    roles[c2] = RoleSkip;
                    names[c2] = "";
                }
                foreach (var col in columnItems)
                {
                    roles[col.Index] = (col.RoleBox.SelectedItem as string) ?? RoleSkip;
                    names[col.Index] = col.NameBox.Text.Trim();
                }

                foreach (var single in new[] { RoleCins, RoleSerial, RoleQuantity, RoleSystem })
                {
                    var howMany = 0;
                    foreach (var roleOfColumn in roles)
                    {
                        if (roleOfColumn == single)
                        {
                            howMany++;
                        }
                    }

                    if (howMany > 1)
                    {
                        warn("Birden fazla sütunu \"" + ShortRole(single) + "\" olarak seçtin.\n" +
                             "Bu seçenek sadece bir sütun için kullanılabilir.");
                        return;
                    }
                }

                // Grup sütunu seçilmediyse tüm ürünler için tek bir grup adı yazılması istenir.
                string typedGroupName = null;
                if (Array.IndexOf(roles, RoleCins) < 0)
                {
                    typedGroupName = AskGroupName(window);
                    if (typedGroupName == null)
                    {
                        return;   // "Geri git, grup sütunu belirle" seçildi: ayar penceresinde kalınır
                    }
                }

                var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var c2 = 0; c2 < width; c2++)
                {
                    if (roles[c2] != RoleOther)
                    {
                        continue;
                    }

                    if (names[c2].Length == 0)
                    {
                        warn("Sütun " + ColumnLetter(c2) + " için bir ad yaz.");
                        return;
                    }

                    if (string.Equals(names[c2], AdetHeader, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(names[c2], SiraNoHeader, StringComparison.OrdinalIgnoreCase))
                    {
                        warn("\"" + names[c2] + "\" adı özel bir addır (Sütun " + ColumnLetter(c2) + ").\n" +
                             "Adet sütunuysa \"Bu sütunda ne yazıyor?\" kutusundan \"Adet\" seç, değilse başka bir ad ver.");
                        return;
                    }

                    if (!usedNames.Add(names[c2]))
                    {
                        warn("\"" + names[c2] + "\" adı birden fazla sütunda kullanılmış.\n" +
                             "Her sütunun adı farklı olmalı.");
                        return;
                    }
                }

                // Sütun sırası korunur; kullanılmayan sütunların adı boş bırakılır (yok sayılır).
                var headerLine = new string[width];
                for (var c2 = 0; c2 < width; c2++)
                {
                    if (roles[c2] == RoleCins)
                    {
                        headerLine[c2] = CinsHeader;
                    }
                    else if (roles[c2] == RoleSerial)
                    {
                        headerLine[c2] = names[c2].Length > 0 ? names[c2] : SeriNoHeader;
                    }
                    else if (roles[c2] == RoleQuantity)
                    {
                        headerLine[c2] = AdetHeader;
                    }
                    else if (roles[c2] == RoleSystem)
                    {
                        headerLine[c2] = SistemIsmiHeader;
                    }
                    else if (roles[c2] == RoleOther)
                    {
                        headerLine[c2] = names[c2];
                    }
                    else
                    {
                        headerLine[c2] = "";
                    }
                }

                var table = new List<string[]> { headerLine };
                foreach (var dataRow in dataRows)
                {
                    var line = new string[width];
                    for (var c2 = 0; c2 < width; c2++)
                    {
                        line[c2] = c2 < dataRow.Length ? dataRow[c2] : "";
                    }
                    table.Add(line);
                }

                // Grup adı elle yazıldıysa tabloya sona bir "Cins" sütunu eklenir ve ürünlerin hepsine yazılır.
                // Seçilen sütunların hiçbirinde verisi olmayan satırlara yazılmaz (boş satır sayılır).
                if (typedGroupName != null)
                {
                    for (var t = 0; t < table.Count; t++)
                    {
                        var extended = new string[width + 1];
                        Array.Copy(table[t], extended, width);

                        if (t == 0)
                        {
                            extended[width] = CinsHeader;
                        }
                        else
                        {
                            var rowHasValue = false;
                            for (var c2 = 0; c2 < width; c2++)
                            {
                                if (roles[c2] != RoleSkip && (table[t][c2] ?? "").Trim().Length > 0)
                                {
                                    rowHasValue = true;
                                    break;
                                }
                            }
                            extended[width] = rowHasValue ? typedGroupName : "";
                        }

                        table[t] = extended;
                    }
                }

                normalizedResult = table;
                chosenCins = typedGroupName != null ? width : Array.IndexOf(roles, RoleCins);
                chosenSerial = Array.IndexOf(roles, RoleSerial);
                chosenSystem = Array.IndexOf(roles, RoleSystem);

                window.DialogResult = true;
            };

            // ---------- Yerleştir ----------
            root.Children.Add(step1);
            root.Children.Add(buttons);
            root.Children.Add(legend);
            root.Children.Add(previewScroll);
            root.Children.Add(settingsPanel);
            root.Children.Add(step2);
            root.Children.Add(groupNote);
            root.Children.Add(columnsTitle);
            root.Children.Add(columnsScroll);
            window.Content = root;

            refreshPreview();
            applyDefaults();

            if (window.ShowDialog() != true || normalizedResult == null)
            {
                return null;
            }

            cinsColumn = chosenCins;
            serialColumn = chosenSerial;
            systemColumn = chosenSystem;
            return normalizedResult;
        }

        // =====================================================================
        // ÖN BİLGİ EKRANI (dosya ve sayfa seçildikten sonra, ayar penceresinden önce)
        // =====================================================================
        //
        // Ne yapılacağını ve ne olacağını her maddenin altında kendi örneğiyle kısaca anlatır.
        // "Anladım, devam et" derse ayar penceresine geçilir; "İptal" derse içe aktarma durur.

        private bool ShowImportInfo(string title, bool forScrap)
        {
            var window = new Window
            {
                Title = title + " — Başlamadan Önce",
                Width = 700,
                Height = 760,
                MinWidth = 560,
                MinHeight = 420,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ShowInTaskbar = false,
                Background = Brushes.White,
                FontSize = 13
            };

            var headerRowBrush = new SolidColorBrush(Color.FromRgb(0xCF, 0xE8, 0xFF));
            var dataRowBrush = new SolidColorBrush(Color.FromRgb(0xDF, 0xF5, 0xDF));

            var panel = new StackPanel { Margin = new Thickness(18, 14, 18, 8) };

            // ---------- Ne yapacaksın? ----------
            panel.Children.Add(MakeInfoHeading("Ne yapacaksın?"));

            // 1) Satırları belirle
            panel.Children.Add(MakeBlinkLine("1) Satırları belirle:",
                "Excel'de üstte İL, CİNS, SERİ NO gibi başlıkların yazdığı satırın numarasını ve " +
                "ürünlerin başladığı satırın numarasını yaz."));

            var rowsTable = MakeMiniTable(
                new[]
                {
                    new[] { "Satır", "A", "B", "C", "D" },
                    new[] { "1", "İL", "CİNS", "SERİ NO", "MARKA" },
                    new[] { "2", "ÇORUM", "YAZICI", "4E89BKBQ900007N", "SAMSUNG" },
                    new[] { "3", "ÇORUM", "TABLET", "T9A3HD00XQ", "CETRİX" }
                },
                new Brush[] { Brushes.WhiteSmoke, headerRowBrush, dataRowBrush, dataRowBrush },
                true);

            var tags = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            tags.Inlines.Add(new System.Windows.Documents.Run(" Mavi ") { Background = headerRowBrush, FontWeight = FontWeights.SemiBold });
            tags.Inlines.Add(new System.Windows.Documents.Run(" 1. satır başlıklar (İL, CİNS...) → "));
            tags.Inlines.Add(new System.Windows.Documents.Run("1") { FontWeight = FontWeights.Bold });
            tags.Inlines.Add(new System.Windows.Documents.Run(" yaz.     "));
            tags.Inlines.Add(new System.Windows.Documents.Run(" Yeşil ") { Background = dataRowBrush, FontWeight = FontWeights.SemiBold });
            tags.Inlines.Add(new System.Windows.Documents.Run(" ürünler 2. satırdan başlıyor → "));
            tags.Inlines.Add(new System.Windows.Documents.Run("2") { FontWeight = FontWeights.Bold });
            tags.Inlines.Add(new System.Windows.Documents.Run(" yaz."));

            panel.Children.Add(MakeExampleBox(rowsTable, tags));

            // 2) Sütunları tanıt
            panel.Children.Add(MakeBlinkLine("2) Sütunları tanıt:",
                "Her sütunun ne olduğunu seç."));

            var rolesTable = MakeMiniTable(
                new[]
                {
                    new[] { "Sütunda yazan", "", "Ne seçilir?" },
                    new[] { "YAZICI", "→", "Grup (ürünün türü)" },
                    new[] { "4E89BKBQ900007N", "→", "Seri No" },
                    new[] { "SAMSUNG", "→", "Diğer özellik (adı: Marka)" }
                },
                new Brush[] { Brushes.WhiteSmoke, null, null, null },
                false);
            panel.Children.Add(MakeExampleBox(rolesTable));

            // 3) Devam'a bas
            var step3 = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 10, 0, 2) };
            step3.Inlines.Add(new System.Windows.Documents.Run("3) Devam'a bas:") { FontWeight = FontWeights.Bold });
            step3.Inlines.Add(new System.Windows.Documents.Run(" Program önce bir özet gösterir."));
            panel.Children.Add(step3);

            var summaryText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontStyle = FontStyles.Italic,
                Text = (forScrap ? "2 satır HURDAYA eklenecek" : "2 ürün depoya eklenecek") +
                       ": YAZICI 1, TABLET 1. Devam edilsin mi?"
            };

            var fakeButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            fakeButtons.Children.Add(MakeFakeButton("Evet"));
            fakeButtons.Children.Add(MakeFakeButton("Hayır"));

            var noChange = new TextBlock
            {
                Text = "\"Hayır\" dersen hiçbir şey eklenmez.",
                Foreground = Brushes.DimGray,
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 0)
            };
            panel.Children.Add(MakeExampleBox(summaryText, fakeButtons, noChange));

            // ---------- Ne olacak? ----------
            panel.Children.Add(MakeInfoHeading("Ne olacak?"));

            if (forScrap)
            {
                panel.Children.Add(MakeInfoText("• Ürünler HURDAYA eklenir. Gerçek depoya hiçbir şey eklenmez ve değişmez."));
                panel.Children.Add(MakeExampleBox(MakeInfoText("YAZICI hurda listesinde görünür, depodaki ürünler olduğu gibi kalır.")));
            }
            else
            {
                panel.Children.Add(MakeInfoText("• Ürünler depoya eklenir."));
                panel.Children.Add(MakeExampleBox(MakeInfoText("YAZICI, \"Yazıcı\" grubuna depoya eklenir.")));
            }

            panel.Children.Add(MakeInfoText("• Aynı seri no'lu satırların hepsi eklenir."));
            panel.Children.Add(MakeExampleBox(MakeInfoText("4 satırda \"FFF\" yazıyorsa dördü de eklenir, özette bilgi verilir.")));

            panel.Children.Add(MakeInfoText("• Excel dosyan değişmez, sadece okunur."));
            panel.Children.Add(MakeExampleBox(MakeInfoText("İçe aktardıktan sonra dosyayı olduğu gibi saklayabilirsin.")));

            var safeText = new TextBlock { TextWrapping = TextWrapping.Wrap };
            safeText.Inlines.Add(new System.Windows.Documents.Run("İstediğin an "));
            safeText.Inlines.Add(new System.Windows.Documents.Run("İptal") { FontWeight = FontWeights.Bold });
            safeText.Inlines.Add(new System.Windows.Documents.Run("'e basabilirsin, hiçbir şey değişmez."));
            panel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xB2, 0xEB, 0xF2)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 14, 0, 6),
                Child = safeText
            });

            // ---------- Butonlar ----------
            var okButton = new Button
            {
                Content = "Anladım, devam et",
                Width = 150,
                Padding = new Thickness(0, 6, 0, 6),
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x6F, 0xD4))
            };
            okButton.Click += delegate { window.DialogResult = true; };

            var cancelButton = new Button { Content = "İptal", Width = 90, Padding = new Thickness(0, 6, 0, 6), IsCancel = true };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 18, 12)
            };
            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);
            DockPanel.SetDock(buttons, Dock.Bottom);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = panel
            };

            var root = new DockPanel();
            root.Children.Add(buttons);
            root.Children.Add(scroll);
            window.Content = root;

            return window.ShowDialog() == true;
        }

        private static TextBlock MakeInfoHeading(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                Margin = new Thickness(0, 12, 0, 4)
            };
        }

        private static TextBlock MakeInfoText(string text)
        {
            return new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 8, 0, 0) };
        }

        /// <summary>Örnekleri gösteren, solunda mavi çizgisi olan açık mavi kutu.</summary>
        private static UIElement MakeExampleBox(params UIElement[] content)
        {
            var inner = new StackPanel();
            inner.Children.Add(new TextBlock
            {
                Text = "Örnek:",
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.DimGray,
                Margin = new Thickness(0, 0, 0, 4)
            });
            foreach (var element in content)
            {
                inner.Children.Add(element);
            }

            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xE6, 0xF6, 0xF9)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x6F, 0xD4)),
                BorderThickness = new Thickness(3, 0, 0, 0),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(6, 4, 0, 2),
                Child = inner
            };
        }

        /// <summary>Küçük örnek tablosu. Her satırın zemin rengi verilir (null = renksiz).</summary>
        private static UIElement MakeMiniTable(string[][] cells, Brush[] rowBrushes, bool boldFirstColumn)
        {
            var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left };

            var columnCount = 0;
            foreach (var line in cells)
            {
                if (line.Length > columnCount)
                {
                    columnCount = line.Length;
                }
            }
            for (var c = 0; c < columnCount; c++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            }

            for (var r = 0; r < cells.Length; r++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                for (var c = 0; c < cells[r].Length; c++)
                {
                    var cell = MakePreviewCell(cells[r][c], r == 0 || (boldFirstColumn && c == 0));
                    if (r < rowBrushes.Length && rowBrushes[r] != null)
                    {
                        cell.Background = rowBrushes[r];
                    }
                    Grid.SetRow(cell, r);
                    Grid.SetColumn(cell, c);
                    grid.Children.Add(cell);
                }
            }

            return grid;
        }

        private static UIElement MakeFakeButton(string text)
        {
            return new Border
            {
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Background = Brushes.WhiteSmoke,
                Padding = new Thickness(14, 1, 14, 1),
                Margin = new Thickness(0, 0, 6, 0),
                Child = new TextBlock { Text = text, FontSize = 12 }
            };
        }

        /// <summary>Başlığı yumuşakça turuncuya dönüp sönen (dikkat çeken) kısa adım cümlesi.</summary>
        private static UIElement MakeBlinkLine(string title, string text)
        {
            var backColor = Color.FromRgb(0xFF, 0xE0, 0xB2);   // zemin (açık turuncu)
            var textColor = Color.FromRgb(0xD8, 0x43, 0x15);   // yazı (koyu turuncu)

            var back = new SolidColorBrush(Color.FromArgb(0, backColor.R, backColor.G, backColor.B));
            var fore = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C));

            var line = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = fore,
                Padding = new Thickness(6, 3, 6, 3)
            };
            line.Inlines.Add(new System.Windows.Documents.Run(title) { FontWeight = FontWeights.Bold });
            line.Inlines.Add(new System.Windows.Documents.Run(" " + text));

            var backAnimation = new System.Windows.Media.Animation.ColorAnimation
            {
                From = Color.FromArgb(0, backColor.R, backColor.G, backColor.B),
                To = backColor,
                Duration = new Duration(TimeSpan.FromMilliseconds(700)),
                AutoReverse = true,
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                EasingFunction = new System.Windows.Media.Animation.SineEase()
            };
            back.BeginAnimation(SolidColorBrush.ColorProperty, backAnimation);

            var foreAnimation = new System.Windows.Media.Animation.ColorAnimation
            {
                From = Color.FromRgb(0x1C, 0x1C, 0x1C),
                To = textColor,
                Duration = new Duration(TimeSpan.FromMilliseconds(700)),
                AutoReverse = true,
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                EasingFunction = new System.Windows.Media.Animation.SineEase()
            };
            fore.BeginAnimation(SolidColorBrush.ColorProperty, foreAnimation);

            return new Border
            {
                Background = back,
                CornerRadius = new CornerRadius(5),
                Child = line,
                Margin = new Thickness(0, 10, 0, 2),
                HorizontalAlignment = HorizontalAlignment.Left
            };
        }

        /// <summary>
        /// Grup sütunu seçilmediğinde tüm ürünler için tek bir grup adı ister.
        /// "Geri git, grup sütunu belirle" seçilirse null döner (ayar penceresine dönülür).
        /// </summary>
        private static string AskGroupName(Window owner)
        {
            var dialog = new Window
            {
                Title = "Grup adı yaz",
                Width = 460,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                Background = Brushes.White,
                FontSize = 13
            };

            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock
            {
                Text = "Grup (ürünün türü) sütununu seçmedin.",
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Bu dosyadaki grup sütunu yoksa tüm ürünler için bir grup adı yaz.\nÖrnek: Telsiz, Bilgisayar, Yazıcı vs.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 8)
            });

            var input = new TextBox { Padding = new Thickness(4, 3, 4, 3) };
            panel.Children.Add(input);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var okButton = new Button
            {
                Content = "Tamam",
                Width = 90,
                Padding = new Thickness(0, 5, 0, 5),
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };
            var backButton = new Button
            {
                Content = "Geri git, grup sütunu belirle",
                Padding = new Thickness(12, 5, 12, 5),
                IsCancel = true
            };
            buttons.Children.Add(okButton);
            buttons.Children.Add(backButton);
            panel.Children.Add(buttons);

            string result = null;

            okButton.Click += delegate
            {
                var text = input.Text.Trim();
                if (text.Length == 0)
                {
                    MessageBox.Show(dialog,
                        "Bir grup adı yaz ya da geri gidip grup sütununu belirle.",
                        "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                result = text;
                dialog.DialogResult = true;
            };

            backButton.Click += delegate
            {
                dialog.DialogResult = false;
            };

            dialog.Content = panel;
            dialog.Loaded += delegate { input.Focus(); };
            dialog.ShowDialog();

            return result;
        }

        /// <summary>Zemini koyu kırmızıya dönüp sönen (yanıp sönen) kısa yazı. Yazı rengi kırmızı zeminde beyaza döner.</summary>
        private static UIElement MakeRedBlinkLabel(string text)
        {
            var backColor = Color.FromRgb(0x8B, 0x00, 0x00);   // koyu kırmızı zemin

            var back = new SolidColorBrush(Color.FromArgb(0, backColor.R, backColor.G, backColor.B));
            var fore = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C));

            var label = new TextBlock
            {
                Text = text,
                Foreground = fore,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(6, 2, 6, 2)
            };

            var backAnimation = new System.Windows.Media.Animation.ColorAnimation
            {
                From = Color.FromArgb(0, backColor.R, backColor.G, backColor.B),
                To = backColor,
                Duration = new Duration(TimeSpan.FromMilliseconds(700)),
                AutoReverse = true,
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                EasingFunction = new System.Windows.Media.Animation.SineEase()
            };
            back.BeginAnimation(SolidColorBrush.ColorProperty, backAnimation);

            var foreAnimation = new System.Windows.Media.Animation.ColorAnimation
            {
                From = Color.FromRgb(0x1C, 0x1C, 0x1C),
                To = Colors.White,
                Duration = new Duration(TimeSpan.FromMilliseconds(700)),
                AutoReverse = true,
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                EasingFunction = new System.Windows.Media.Animation.SineEase()
            };
            fore.BeginAnimation(SolidColorBrush.ColorProperty, foreAnimation);

            return new Border
            {
                Background = back,
                CornerRadius = new CornerRadius(4),
                Child = label,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        /// <summary>"Hangi sütuna ne seçmeliyim?" yardım penceresi.</summary>
        private void ShowColumnHelp(Window owner)
        {
            var help = new Window
            {
                Title = "Hangi sütuna ne seçmeliyim?",
                Width = 640,
                Height = 560,
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                Background = Brushes.White,
                FontSize = 13
            };

            var items = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Grup",
                    "Ürünün ne olduğunu söyleyen sütun. Örnek: KİŞİSEL BİLGİSAYAR, FOTOKOPİ MAKİNASI, YAZICI. " +
                    "Program ürünleri bu sütuna göre gruplar, bu yüzden çok önemlidir. Böyle bir sütun yoksa hiçbirini seçme; " +
                    "Devam'a basınca program tüm ürünler için bir grup adı yazmanı ister (örnek: Telsiz)."),
                new KeyValuePair<string, string>("Seri No",
                    "Her ürüne özel numara. Örnek: CZC7338W2N, 4E89BKBQ900007N. Başlık olarak genelde Seri No, " +
                    "Seri Numarası ya da S/N yazar. Aynı seri no birden fazla satırda olsa da hepsi eklenir."),
                new KeyValuePair<string, string>("Sistem Adı (ETMYS Adı)",
                    "Ürünün sistemdeki kayıtlı adı. Örnek: LENOVO BİLGİSAYAR, SAMSUNG ML-3471 ND."),
                new KeyValuePair<string, string>("Adet",
                    "Aynı üründen kaç tane olduğunu gösteren sayı. Bu sütun yoksa her satır 1 adet sayılır."),
                new KeyValuePair<string, string>("Diğer özellik",
                    "Marka, model, işlemci, RAM gibi başka bilgiler. \"Program bu bilgiye ne ad versin?\" " +
                    "kutusuna o bilginin adını yaz (örnek: Marka).")
            };

            var panel = new StackPanel { Margin = new Thickness(16) };
            foreach (var pair in items)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = pair.Key,
                    FontWeight = FontWeights.Bold,
                    FontSize = 14,
                    Margin = new Thickness(0, 10, 0, 2)
                });
                panel.Children.Add(new TextBlock { Text = pair.Value, TextWrapping = TextWrapping.Wrap });
            }

            var closeButton = new Button
            {
                Content = "Tamam",
                Width = 90,
                Padding = new Thickness(0, 5, 0, 5),
                Margin = new Thickness(0, 8, 14, 12),
                HorizontalAlignment = HorizontalAlignment.Right,
                IsDefault = true,
                IsCancel = true
            };
            closeButton.Click += delegate { help.Close(); };
            DockPanel.SetDock(closeButton, Dock.Bottom);

            var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            var dock = new DockPanel();
            dock.Children.Add(closeButton);
            dock.Children.Add(scroll);
            help.Content = dock;
            help.ShowDialog();
        }

        /// <summary>
        /// Çizgili açıklama kutusu: adım cümlesi bir renge, örnek satırı başka bir renge yumuşakça dönüp söner.
        /// Renkleri değiştirmek için aşağıdaki dört satır yeterlidir.
        /// </summary>
        private static UIElement MakeStepBanner(string title, string text, string example)
        {
            var stepBackColor = Color.FromRgb(0xFF, 0xE0, 0xB2);   // adım cümlesinin zemini (açık turuncu)
            var stepTextColor = Color.FromRgb(0xD8, 0x43, 0x15);   // adım cümlesinin yazısı (koyu turuncu)
            var stepLineColor = Color.FromRgb(0xE6, 0x51, 0x00);   // kutunun sol çizgisi (turuncu)
            var exampleBackColor = Color.FromRgb(0xB2, 0xEB, 0xF2); // örnek satırının zemini (açık mavi)

            var back = new SolidColorBrush(Color.FromArgb(0, stepBackColor.R, stepBackColor.G, stepBackColor.B));
            var fore = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C));

            var line = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = fore,
                Padding = new Thickness(6, 3, 6, 3)
            };
            line.Inlines.Add(new System.Windows.Documents.Run(title) { FontWeight = FontWeights.Bold });
            line.Inlines.Add(new System.Windows.Documents.Run(" " + text));

            var pulse = new Border
            {
                Background = back,
                CornerRadius = new CornerRadius(5),
                Child = line,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            var backAnimation = new System.Windows.Media.Animation.ColorAnimation
            {
                From = Color.FromArgb(0, stepBackColor.R, stepBackColor.G, stepBackColor.B),
                To = stepBackColor,
                Duration = new Duration(TimeSpan.FromMilliseconds(700)),
                AutoReverse = true,
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                EasingFunction = new System.Windows.Media.Animation.SineEase()
            };
            back.BeginAnimation(SolidColorBrush.ColorProperty, backAnimation);

            var foreAnimation = new System.Windows.Media.Animation.ColorAnimation
            {
                From = Color.FromRgb(0x1C, 0x1C, 0x1C),
                To = stepTextColor,
                Duration = new Duration(TimeSpan.FromMilliseconds(700)),
                AutoReverse = true,
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                EasingFunction = new System.Windows.Media.Animation.SineEase()
            };
            fore.BeginAnimation(SolidColorBrush.ColorProperty, foreAnimation);

            // Örnek satırı da yumuşakça yanıp sönen bir zemin üzerinde durur.
            var exampleBack = new SolidColorBrush(Color.FromArgb(0, exampleBackColor.R, exampleBackColor.G, exampleBackColor.B));
            var exampleBorder = new Border
            {
                Background = exampleBack,
                CornerRadius = new CornerRadius(5),
                Margin = new Thickness(0, 3, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock
                {
                    Text = example,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Padding = new Thickness(6, 3, 6, 3)
                }
            };

            var exampleAnimation = new System.Windows.Media.Animation.ColorAnimation
            {
                From = Color.FromArgb(0, exampleBackColor.R, exampleBackColor.G, exampleBackColor.B),
                To = exampleBackColor,
                Duration = new Duration(TimeSpan.FromMilliseconds(700)),
                AutoReverse = true,
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                EasingFunction = new System.Windows.Media.Animation.SineEase()
            };
            exampleBack.BeginAnimation(SolidColorBrush.ColorProperty, exampleAnimation);

            var panel = new StackPanel();
            panel.Children.Add(pulse);
            panel.Children.Add(exampleBorder);

            return new Border
            {
                BorderBrush = new SolidColorBrush(stepLineColor),
                BorderThickness = new Thickness(4, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3)),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 8),
                Child = panel
            };
        }

        private static UIElement MakeLegendItem(Brush color, string text)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0) };
            panel.Children.Add(new Border
            {
                Width = 14,
                Height = 14,
                Background = color,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 5, 0)
            });
            panel.Children.Add(new TextBlock { Text = text, Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center });
            return panel;
        }

        private static Border MakePreviewCell(string text, bool bold)
        {
            var block = new TextBlock
            {
                Text = text,
                Padding = new Thickness(4, 2, 4, 2),
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal
            };

            if (text.Length > 0)
            {
                block.ToolTip = text;
            }

            return new Border
            {
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(0, 0, 1, 1),
                Child = block
            };
        }

        /// <summary>"Grup (ürünün türü)" -> "Grup": mesajlarda kısa ad göstermek için.</summary>
        private static string ShortRole(string role)
        {
            var open = role.IndexOf(" (", StringComparison.Ordinal);
            return open > 0 ? role.Substring(0, open) : role;
        }

        /// <summary>Sütun sırasını Excel harfine çevirir: 0 = A, 25 = Z, 26 = AA...</summary>
        private static string ColumnLetter(int index)
        {
            var letters = "";
            var n = index;
            while (n >= 0)
            {
                letters = (char)('A' + (n % 26)) + letters;
                n = n / 26 - 1;
            }
            return letters;
        }

        /// <summary>Sütun adına göre hazır seçili gelecek rol (kullanıcı değiştirebilir).</summary>
        private static string DefaultRoleFor(string headerText)
        {
            if (headerText.Length == 0)
            {
                return RoleSkip;
            }
            if (string.Equals(headerText, CinsHeader, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(headerText, "Grup", StringComparison.OrdinalIgnoreCase))
            {
                return RoleCins;
            }
            if (string.Equals(headerText, SeriNoHeader, StringComparison.OrdinalIgnoreCase))
            {
                return RoleSerial;
            }
            if (string.Equals(headerText, AdetHeader, StringComparison.OrdinalIgnoreCase))
            {
                return RoleQuantity;
            }
            if (string.Equals(headerText, SistemIsmiHeader, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(headerText, "Sistem Adı", StringComparison.OrdinalIgnoreCase))
            {
                return RoleSystem;
            }
            if (string.Equals(headerText, SiraNoHeader, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(headerText, ScrapDateHeader, StringComparison.OrdinalIgnoreCase))
            {
                return RoleSkip;
            }
            return RoleOther;
        }

        /// <summary>Ürün satırlarındaki ilk 3 dolu değer (sütunun ne içerdiğini göstermek için).</summary>
        private static string SampleText(List<string[]> rows, List<int> numbers, int column, Func<int, bool> isDataRow)
        {
            var samples = new List<string>();

            for (var i = 0; i < rows.Count && samples.Count < 3; i++)
            {
                if (!isDataRow(numbers[i]))
                {
                    continue;
                }

                var value = Cell(rows[i], column);
                if (value.Length == 0)
                {
                    continue;
                }

                if (value.Length > 24)
                {
                    value = value.Substring(0, 24) + "...";
                }
                samples.Add(value);
            }

            return samples.Count == 0 ? EmptySample : string.Join("   |   ", samples);
        }
    }
}