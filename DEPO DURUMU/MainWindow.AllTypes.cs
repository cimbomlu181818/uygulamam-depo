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
                Title = "Tüm Cinsleri Dışa Aktar",
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
        // İÇE AKTAR
        // =====================================================================

        private void ImportAllTypesMenu_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Tüm Cinsleri İçe Aktar",
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

            if (rows.Count < 2)
            {
                MessageBox.Show("Dosyada, başlık satırından sonra en az bir veri satırı olmalı.",
                    "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var headers = rows[0].Select(h => h.Trim()).ToArray();

            // Kullanıcıya gösterilecek seçenekler: başlığı olan her sütun (yanında bir örnek değer).
            var pickable = new List<KeyValuePair<int, string>>();
            for (var i = 0; i < headers.Length; i++)
            {
                if (headers[i].Length > 0)
                {
                    pickable.Add(new KeyValuePair<int, string>(i, DescribeColumn(headers[i], rows, i)));
                }
            }

            if (pickable.Count == 0)
            {
                MessageBox.Show("Dosyanın ilk satırında başlık bulunamadı.",
                    "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 1. SORU: Cins sütunu hangisi?
            var cinsColumn = PickColumn("Cins sütunu",
                "Hangi sütun ürünün CİNSİNİ içeriyor?\n(Örnek: Bilgisayar, Telsiz)",
                pickable, FindOptionIndex(pickable, headers, CinsHeader));
            if (cinsColumn < 0)
            {
                return;
            }

            // 2. SORU: Sistem adı sütunu var mı? Varsa hangisi?
            var systemOptions = pickable.Where(o => o.Key != cinsColumn).ToList();
            var systemColumn = -1;

            if (systemOptions.Count > 0)
            {
                var systemAnswer = MessageBox.Show(this,
                    "Dosyada sistem adı (sistem ismi) sütunu var mı?\n\n" +
                    "Evet: var, bir sonraki adımda sütunu seçeceksin.\n" +
                    "Hayır: yok.\n" +
                    "İptal: içe aktarmayı durdur.",
                    "Depo Durumu", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                if (systemAnswer == MessageBoxResult.Cancel || systemAnswer == MessageBoxResult.None)
                {
                    return;
                }

                if (systemAnswer == MessageBoxResult.Yes)
                {
                    var preselect = FindOptionIndex(systemOptions, headers, SistemIsmiHeader);
                    if (preselect < 0)
                    {
                        preselect = FindOptionIndex(systemOptions, headers, "Sistem Adı");
                    }

                    systemColumn = PickColumn("Sistem adı sütunu",
                        "Hangi sütun SİSTEM ADINI içeriyor?\n(Değerler uygulamadaki \"Sistem İsmi\" özelliğine yazılır.)",
                        systemOptions, preselect);
                    if (systemColumn < 0)
                    {
                        return;
                    }
                }
            }

            // 3. SORU: Seri numarası sütunu var mı? Varsa hangisi?
            var serialOptions = pickable.Where(o => o.Key != cinsColumn && o.Key != systemColumn).ToList();
            var serialColumn = -1;

            if (serialOptions.Count > 0)
            {
                var serialAnswer = MessageBox.Show(this,
                    "Dosyada seri numarası sütunu var mı?\n\n" +
                    "Evet: var, bir sonraki adımda sütunu seçeceksin.\n" +
                    "Hayır: yok.\n" +
                    "İptal: içe aktarmayı durdur.",
                    "Depo Durumu", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                if (serialAnswer == MessageBoxResult.Cancel || serialAnswer == MessageBoxResult.None)
                {
                    return;
                }

                if (serialAnswer == MessageBoxResult.Yes)
                {
                    serialColumn = PickColumn("Seri numarası sütunu",
                        "Hangi sütun SERİ NUMARASINI içeriyor?\n(Değerler uygulamadaki \"Seri No\" özelliğine yazılır.)",
                        serialOptions, FindOptionIndex(serialOptions, headers, SeriNoHeader));
                    if (serialColumn < 0)
                    {
                        return;
                    }
                }
            }

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
                systemColumn >= 0 ? headers[systemColumn] : null, blankTypeRows))
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
                message.AppendLine("Yeni oluşturulan cinsler: " + JoinLimited(result.CreatedTypes, 15));
            }
            if (result.CreatedProperties.Count > 0)
            {
                message.AppendLine();
                message.AppendLine("Yeni oluşturulan özellikler: " + JoinLimited(result.CreatedProperties, 15));
            }
            if (result.AttachedToExistingTypes.Count > 0)
            {
                message.AppendLine();
                message.AppendLine("Var olan cinslere eklenen özellikler: " + JoinLimited(result.AttachedToExistingTypes, 15));
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
                message.AppendLine("Cins hücresi boş olan " + blankTypeRows + " satır \"" + UnknownTypeName +
                                   "\" cinsine eklendi (seri no çakışanlar hariç).");
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
            string serialHeader, string systemHeader, int blankTypeRows)
        {
            var text = new StringBuilder();
            text.AppendLine("Dosyadan " + plans.Sum(p => p.Rows.Count) + " ürün satırı okundu.");

            var newTypes = plans.Where(p => p.Existing == null).ToList();
            if (newTypes.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("Yeni oluşturulacak cinsler (" + newTypes.Count + "):");
                foreach (var plan in newTypes.Take(10))
                {
                    text.AppendLine("   • " + plan.Name + " — özellikler: " +
                                    JoinLimited(plan.AttachColumns.Select(c => c.PropertyName), 8));
                }
                if (newTypes.Count > 10)
                {
                    text.AppendLine("   ... ve " + (newTypes.Count - 10) + " cins daha");
                }
            }

            var newProperties = columns.Where(c => c.Property == null).Select(c => c.PropertyName + " (" + c.NewDataType + ")").ToList();
            if (newProperties.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("Kütüphaneye yeni eklenecek özellikler (" + newProperties.Count + "): " + JoinLimited(newProperties, 12));
            }

            var extended = plans.Where(p => p.Existing != null && p.AttachColumns.Count > 0).ToList();
            if (extended.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("Var olan cinslere yeni özellik eklenecek:");
                foreach (var plan in extended.Take(10))
                {
                    text.AppendLine("   • " + plan.Name + ": " + JoinLimited(plan.AttachColumns.Select(c => c.PropertyName), 8));
                }
                if (extended.Count > 10)
                {
                    text.AppendLine("   ... ve " + (extended.Count - 10) + " cins daha");
                }
            }

            if (serialHeader != null)
            {
                var serialColumnItem = columns.FirstOrDefault(c => c.IsSerial);
                text.AppendLine();
                text.AppendLine("Seri numarası: \"" + serialHeader + "\" sütunu, \"" +
                                (serialColumnItem != null ? serialColumnItem.PropertyName : SeriNoHeader) +
                                "\" özelliğine yazılacak. Aynı seri no'lu ürünler eklenmez.");
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
                text.AppendLine("Cins hücresi boş " + blankTypeRows + " satır, \"" + UnknownTypeName +
                                "\" cinsine eklenecek.");
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

                // Seri No tekilliği için, depodaki ve hurdadaki mevcut seri no'lar bir kez belleğe okunur
                // (her satır için veritabanına sormaktan çok daha hızlı). Eklenen yeni seri no'lar da
                // kümeye girer; böylece aynı dosyadaki tekrarlar da yakalanır.
                var scrapSerials = LoadStringSet(connection,
                    "SELECT TextValue FROM ScrapProductValues WHERE IsSerialNumber = 1 AND TextValue IS NOT NULL;");
                var serialSets = new Dictionary<int, HashSet<string>>();
                Func<int, HashSet<string>> getSerialSet = propertyId =>
                {
                    HashSet<string> set;
                    if (!serialSets.TryGetValue(propertyId, out set))
                    {
                        set = LoadStringSet(connection,
                            "SELECT TextValue FROM ProductValues WHERE PropertyId = @propId AND TextValue IS NOT NULL;",
                            new SQLiteParameter("@propId", propertyId));
                        serialSets[propertyId] = set;
                    }
                    return set;
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

                        // Seri No tekilliği: depoda ve hurdada kontrol et (uygulamadaki kuralla aynı).
                        var conflict = false;
                        foreach (var serialCheck in plan.WriteColumns)
                        {
                            if (!serialCheck.IsSerialNumberProperty)
                            {
                                continue;
                            }

                            var serialValue = Cell(cells, serialCheck.Index);
                            if (serialValue.Length == 0)
                            {
                                continue;
                            }

                            if (getSerialSet(serialCheck.Property.Id).Contains(serialValue) ||
                                scrapSerials.Contains(serialValue))
                            {
                                result.SkippedSerials.Add(serialValue);
                                conflict = true;
                            }
                        }

                        if (conflict)
                        {
                            continue;
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

                            if (write.IsSerialNumberProperty && value.Length > 0)
                            {
                                getSerialSet(write.Property.Id).Add(value);
                            }
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

            return ParseCsvText(text, DetectSeparator(text));
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
                foreach (XmlNode rowNode in sheet.SelectNodes("//*[local-name()='row']"))
                {
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

            if (Math.Abs(number) < 1e15 && number == Math.Floor(number))
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
        private static List<string[]> ParseCsvText(string text, char separator)
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

            return rows.Where(line => line.Any(cell => cell.Trim().Length > 0)).ToList();
        }

        // =====================================================================
        // HURDAYA İÇE AKTAR (gerçek depoya hiç dokunmaz)
        // =====================================================================
        //
        // Excel/CSV dosyasındaki satırlar doğrudan HURDA tablolarına yazılır. Gerçek depoda
        // cins, özellik ya da ürün oluşturulmaz/değiştirilmez. Hurdadan geri getirirken cins ve
        // özellikler eksikse zaten otomatik oluşturulur. Tarih yazılmaz (boş kalır).

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

            if (rows.Count < 2)
            {
                MessageBox.Show("Dosyada, başlık satırından sonra en az bir veri satırı olmalı.",
                    "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var headers = rows[0].Select(h => h.Trim()).ToArray();

            var pickable = new List<KeyValuePair<int, string>>();
            for (var i = 0; i < headers.Length; i++)
            {
                if (headers[i].Length > 0)
                {
                    pickable.Add(new KeyValuePair<int, string>(i, DescribeColumn(headers[i], rows, i)));
                }
            }

            if (pickable.Count == 0)
            {
                MessageBox.Show("Dosyanın ilk satırında başlık bulunamadı.",
                    "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 1. SORU: Cins sütunu hangisi?
            var cinsColumn = PickColumn("Cins sütunu",
                "Hangi sütun ürünün CİNSİNİ içeriyor?\n(Örnek: Bilgisayar, Telsiz)",
                pickable, FindOptionIndex(pickable, headers, CinsHeader));
            if (cinsColumn < 0)
            {
                return;
            }

            // 2. SORU: Seri numarası sütunu var mı? Varsa hangisi?
            var serialOptions = pickable.Where(o => o.Key != cinsColumn).ToList();
            var serialColumn = -1;

            if (serialOptions.Count > 0)
            {
                var serialAnswer = MessageBox.Show(this,
                    "Dosyada seri numarası sütunu var mı?\n\n" +
                    "Evet: var, bir sonraki adımda sütunu seçeceksin.\n" +
                    "Hayır: yok.\n" +
                    "İptal: içe aktarmayı durdur.",
                    "Depo Durumu", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                if (serialAnswer == MessageBoxResult.Cancel || serialAnswer == MessageBoxResult.None)
                {
                    return;
                }

                if (serialAnswer == MessageBoxResult.Yes)
                {
                    serialColumn = PickColumn("Seri numarası sütunu",
                        "Hangi sütun SERİ NUMARASINI içeriyor?",
                        serialOptions, FindOptionIndex(serialOptions, headers, SeriNoHeader));
                    if (serialColumn < 0)
                    {
                        return;
                    }
                }
            }

            // Sütunları hazırla (özellik adı, veri tipi, seri no işareti).
            var ignoredColumns = new List<string>();
            int quantityColumn;
            var columns = BuildImportColumns(rows, headers, cinsColumn, serialColumn, -1, out quantityColumn, ignoredColumns);

            // Seri No tekilliği: depoda ve hurdada zaten olan seri no'lar tekrar eklenmez.
            HashSet<string> knownSerials;
            using (var connection = Database.OpenConnection())
            {
                knownSerials = LoadStringSet(connection,
                    "SELECT pv.TextValue FROM ProductValues pv " +
                    "JOIN PropertyDefinitions pd ON pd.Id = pv.PropertyId " +
                    "WHERE pd.IsSerialNumber = 1 AND pv.TextValue IS NOT NULL;");
                knownSerials.UnionWith(LoadStringSet(connection,
                    "SELECT TextValue FROM ScrapProductValues WHERE IsSerialNumber = 1 AND TextValue IS NOT NULL;"));
            }

            // Satırları cinslerine göre sırala; her cinsin kendi sırası dosyadaki sırası olur.
            var items = new List<ScrapImportRow>();
            var typeNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var rankByType = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var skippedSerials = new List<string>();
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

                var conflict = false;
                foreach (var serialCheck in columns.Where(c => c.IsSerialNumberProperty))
                {
                    var serialValue = Cell(cells, serialCheck.Index);
                    if (serialValue.Length > 0 && knownSerials.Contains(serialValue))
                    {
                        skippedSerials.Add(serialValue);
                        conflict = true;
                    }
                }

                if (conflict)
                {
                    continue;
                }

                foreach (var serialAdd in columns.Where(c => c.IsSerialNumberProperty))
                {
                    var serialValue = Cell(cells, serialAdd.Index);
                    if (serialValue.Length > 0)
                    {
                        knownSerials.Add(serialValue);
                    }
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
            if (skippedSerials.Count > 0)
            {
                summary.AppendLine();
                summary.AppendLine("Seri no depoda/hurdada zaten olduğu için eklenmeyecek (" +
                                   skippedSerials.Count + "): " + JoinLimited(skippedSerials, 8));
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
                message.AppendLine("Cins hücresi boş olan satırlar \"" + UnknownTypeName + "\" cinsine eklendi.");
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
    }
}