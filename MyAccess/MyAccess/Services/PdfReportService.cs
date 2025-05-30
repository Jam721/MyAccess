using iTextSharp.text;
using iTextSharp.text.pdf;
using Element = iTextSharp.text.Element;
using Font = iTextSharp.text.Font;
using Image = iTextSharp.text.Image;


namespace MyAccess.Services
{
    public class PdfReportService
    {
        public byte[] GeneratePdfReport(
            List<Dictionary<string, object>> data,
            List<DatabaseService.ColumnDefinition> columns,
            string reportType,
            string title,
            string subtitle)
        {
            using (var ms = new MemoryStream())
            {
                // Создаем документ A4 с отступами
                var document = new Document(PageSize.A4, 20, 20, 30, 30);
                var writer = PdfWriter.GetInstance(document, ms);
                
                document.Open();
                
                // Шрифт с поддержкой русского языка
                var baseFont = BaseFont.CreateFont(
                    "c:\\windows\\fonts\\arial.ttf", 
                    BaseFont.IDENTITY_H, 
                    BaseFont.EMBEDDED);
                var font = new Font(baseFont, 10);
                var boldFont = new Font(baseFont, 12, Font.BOLD);
                var titleFont = new Font(baseFont, 16, Font.BOLD);
                
                // Заголовок
                document.Add(new Paragraph(title, titleFont));
                document.Add(new Paragraph(subtitle, boldFont));
                document.Add(new Paragraph(" ")); // Пустая строка
                
                // Создаем таблицу
                var table = new PdfPTable(columns.Count)
                {
                    WidthPercentage = 100,
                    SpacingBefore = 10f,
                    SpacingAfter = 10f
                };
                
                // Заголовки столбцов
                foreach (var column in columns)
                {
                    var cell = new PdfPCell(new Phrase(column.Name, boldFont))
                    {
                        BackgroundColor = new BaseColor(240, 240, 240) // серый фон
                    };
                    table.AddCell(cell);
                }
                
                // Данные таблицы
                foreach (var row in data)
                {
                    foreach (var column in columns)
                    {
                        var value = row.ContainsKey(column.Name) 
                            ? row[column.Name]?.ToString() ?? string.Empty
                            : string.Empty;
                        
                        table.AddCell(new PdfPCell(new Phrase(value, font)));
                    }
                }
                
                document.Add(table);
                document.Close();
                
                return ms.ToArray();
            }
        }
        
        public byte[] GenerateSummaryPdfReport(
            List<Dictionary<string, object>> data,
            List<DatabaseService.ColumnDefinition> columns,
            string tableName,
            int recordCount)
        {
            using (var ms = new MemoryStream())
            {
                // Создаем документ A4 с отступами
                var document = new Document(PageSize.A4, 20, 20, 30, 30);
                var writer = PdfWriter.GetInstance(document, ms);
                
                document.Open();
                
                // Шрифт с поддержкой русского языка
                var baseFont = BaseFont.CreateFont(
                    "c:\\windows\\fonts\\arial.ttf", 
                    BaseFont.IDENTITY_H, 
                    BaseFont.EMBEDDED);
                var font = new Font(baseFont, 10);
                var boldFont = new Font(baseFont, 12, Font.BOLD);
                var titleFont = new Font(baseFont, 16, Font.BOLD);
                
                // Заголовок отчета
                document.Add(new Paragraph("Расширенный сводный отчет", titleFont));
                document.Add(new Paragraph($"Таблица: {tableName}", boldFont));
                document.Add(new Paragraph($"Записей: {recordCount}", font));
                document.Add(new Paragraph($"Сформирован: {DateTime.Now.ToString("dd.MM.yyyy HH:mm")}", font));
                document.Add(Chunk.Newline);
                
                // Общая информация
                var infoTable = new PdfPTable(4)
                {
                    WidthPercentage = 100,
                    SpacingBefore = 10f,
                    SpacingAfter = 10f
                };
                
                AddInfoCell(infoTable, "Всего полей", columns.Count.ToString(), font);
                AddInfoCell(infoTable, "Числовых полей", columns.Count(c => c.IsNumeric).ToString(), font);
                AddInfoCell(infoTable, "Текстовых полей", columns.Count(c => !c.IsNumeric).ToString(), font);
                AddInfoCell(infoTable, "Заполненность данных", CalculateNullPercentage(data, columns) + "%", font);
                
                document.Add(infoTable);
                document.Add(Chunk.Newline);
                
                // Числовые поля
                foreach (var column in columns.Where(c => c.IsNumeric))
                {
                    var values = GetNumericValues(data, column.Name);
                    if (values.Any())
                    {
                        document.Add(new Paragraph(column.Name + " (числовой)", boldFont));
                        
                        // Статистика
                        var statsTable = new PdfPTable(4)
                        {
                            WidthPercentage = 100,
                            SpacingAfter = 10f
                        };
                        
                        AddStatCell(statsTable, "Сумма", values.Sum().ToString("N2"), font);
                        AddStatCell(statsTable, "Среднее", values.Average().ToString("N2"), font);
                        AddStatCell(statsTable, "Минимум", values.Min().ToString("N2"), font);
                        AddStatCell(statsTable, "Максимум", values.Max().ToString("N2"), font);
                        
                        document.Add(statsTable);
                        document.Add(Chunk.Newline);
                    }
                }
                
                // Текстовые поля
                foreach (var column in columns.Where(c => !c.IsNumeric))
                {
                    var values = GetTextValues(data, column.Name);
                    if (values.Any())
                    {
                        document.Add(new Paragraph(column.Name + " (текстовый)", boldFont));
                        
                        // Статистика
                        var statsTable = new PdfPTable(4)
                        {
                            WidthPercentage = 100,
                            SpacingAfter = 10f
                        };
                        
                        AddStatCell(statsTable, "Уникальных", values.Distinct().Count().ToString(), font);
                        AddStatCell(statsTable, "Частота макс.", GetMostFrequentCount(values).ToString(), font);
                        AddStatCell(statsTable, "Ср. длина", GetAverageLength(values).ToString("N1"), font);
                        AddStatCell(statsTable, "", "", font); // Пустая ячейка для выравнивания
                        
                        document.Add(statsTable);
                        
                        // Топ значений
                        var topValues = GetTopValues(values, 3);
                        if (topValues.Any())
                        {
                            document.Add(new Paragraph("Частые значения:", font));
                            
                            var topTable = new PdfPTable(2)
                            {
                                WidthPercentage = 50,
                                HorizontalAlignment = 0 // Выравнивание по левому краю
                            };
                            
                            foreach (var item in topValues)
                            {
                                topTable.AddCell(new Phrase(item.Value, font));
                                topTable.AddCell(new Phrase(item.Count.ToString(), font));
                            }
                            
                            document.Add(topTable);
                        }
                        
                        document.Add(Chunk.Newline);
                    }
                }
                
                document.Close();
                return ms.ToArray();
            }
        }
        
        private void AddInfoCell(PdfPTable table, string label, string value, Font font)
        {
            table.AddCell(new Phrase(label, font));
            table.AddCell(new Phrase(value, font));
        }
        
        private void AddStatCell(PdfPTable table, string label, string value, Font font)
        {
            var cell = new PdfPCell(new Phrase($"{label}: {value}", font))
            {
                Border = Rectangle.NO_BORDER
            };
            table.AddCell(cell);
        }
        
        private double CalculateNullPercentage(List<Dictionary<string, object>> data, List<DatabaseService.ColumnDefinition> columns)
        {
            if (!data.Any()) return 0;
            
            int totalCells = data.Count * columns.Count;
            int filledCells = 0;
            
            foreach (var row in data)
            {
                filledCells += row.Values.Count(v => v != null);
            }
            
            return Math.Round((filledCells / (double)totalCells) * 100, 1);
        }
        
        private List<decimal> GetNumericValues(List<Dictionary<string, object>> data, string columnName)
        {
            var values = new List<decimal>();
            foreach (var row in data)
            {
                if (row.TryGetValue(columnName, out var val) && val != null)
                {
                    if (decimal.TryParse(val.ToString(), out decimal num))
                    {
                        values.Add(num);
                    }
                }
            }
            return values;
        }
        
        private List<string> GetTextValues(List<Dictionary<string, object>> data, string columnName)
        {
            return data
                .Where(row => row.ContainsKey(columnName) && row[columnName] != null)
                .Select(row => row[columnName].ToString())
                .ToList();
        }
        
        private List<(string Value, int Count)> GetTopValues(List<string> values, int topCount)
        {
            return values
                .GroupBy(v => v)
                .OrderByDescending(g => g.Count())
                .Take(topCount)
                .Select(g => (g.Key, g.Count()))
                .ToList();
        }
        
        private int GetMostFrequentCount(List<string> values)
        {
            if (!values.Any()) return 0;
            return values
                .GroupBy(v => v)
                .Max(g => g.Count());
        }
        
        private double GetAverageLength(List<string> values)
        {
            if (!values.Any()) return 0;
            return values.Average(v => v.Length);
        }

        public class ChartImageData
        {
            public string Title { get; set; }
            public string ImageBase64 { get; set; }
        }
        
        public byte[] GenerateChartReportPdf(
            string title,
            string tableName,
            int recordCount,
            string timestamp,
            string chartTitle,
            string chartImageBase64,
            List<ChartDataPdf> chartData,
            decimal totalValue,
            decimal maxValue,
            decimal minValue,
            decimal otherGroupsValue,
            bool hasMoreGroups)
        {
            using (var ms = new MemoryStream())
            {
                // Создаем документ A4 с отступами
                var document = new Document(PageSize.A4, 20, 20, 30, 30);
                var writer = PdfWriter.GetInstance(document, ms);
                
                document.Open();
                
                // Шрифт с поддержкой русского языка
                var baseFont = BaseFont.CreateFont(
                    "c:\\windows\\fonts\\arial.ttf", 
                    BaseFont.IDENTITY_H, 
                    BaseFont.EMBEDDED);
                var font = new Font(baseFont, 10);
                var boldFont = new Font(baseFont, 12, Font.BOLD);
                var titleFont = new Font(baseFont, 16, Font.BOLD);
                var chartTitleFont = new Font(baseFont, 14, Font.BOLD);
                
                // Заголовок отчета
                document.Add(new Paragraph(title, titleFont));
                
                // Информация о таблице
                var infoTable = new PdfPTable(2);
                infoTable.WidthPercentage = 100;
                
                AddInfoCell(infoTable, "Таблица:", tableName, font);
                AddInfoCell(infoTable, "Записей:", recordCount.ToString(), font);
                AddInfoCell(infoTable, "Сформирован:", timestamp, font);
                
                document.Add(infoTable);
                document.Add(Chunk.Newline);
                
                // Заголовок графика
                document.Add(new Paragraph(chartTitle, chartTitleFont));
                document.Add(Chunk.Newline);
                
                // Вставляем изображение графика
                if (!string.IsNullOrEmpty(chartImageBase64))
                {
                    try
                    {
                        var imageData = Convert.FromBase64String(chartImageBase64);
                        var image = Image.GetInstance(imageData);
                        
                        // Масштабируем изображение под ширину страницы
                        if (image.Width > document.PageSize.Width - 40)
                        {
                            image.ScaleToFit(document.PageSize.Width - 40, document.PageSize.Height);
                        }
                        
                        image.Alignment = Image.ALIGN_CENTER;
                        document.Add(image);
                        document.Add(Chunk.Newline);
                    }
                    catch
                    {
                        document.Add(new Paragraph("Не удалось загрузить изображение графика", font));
                    }
                }
                
                // Таблица с данными
                if (chartData != null && chartData.Any())
                {
                    document.Add(new Paragraph("Данные графика:", boldFont));
                    
                    var dataTable = new PdfPTable(3)
                    {
                        WidthPercentage = 100,
                        SpacingBefore = 10f,
                        SpacingAfter = 10f
                    };
                    
                    // Заголовки таблицы
                    dataTable.AddCell(new PdfPCell(new Phrase("Группа", boldFont)) 
                        { BackgroundColor = new BaseColor(240, 240, 240) });
                    dataTable.AddCell(new PdfPCell(new Phrase("Значение", boldFont)) 
                        { BackgroundColor = new BaseColor(240, 240, 240) });
                    dataTable.AddCell(new PdfPCell(new Phrase("% от общего", boldFont)) 
                        { BackgroundColor = new BaseColor(240, 240, 240) });
                    
                    // Данные
                    foreach (var item in chartData.OrderByDescending(d => d.Value))
                    {
                        if (item.Label == "Другие группы" && hasMoreGroups) continue;
                        
                        dataTable.AddCell(new Phrase(item.Label, font));
                        dataTable.AddCell(new Phrase(item.Value.ToString("N2"), font));
                        dataTable.AddCell(new Phrase(
                            (totalValue > 0 ? (item.Value / totalValue * 100).ToString("N1") : "0") + "%", 
                            font));
                    }
                    
                    // Остальные группы
                    if (hasMoreGroups)
                    {
                        dataTable.AddCell(new Phrase("Другие группы", font));
                        dataTable.AddCell(new Phrase(otherGroupsValue.ToString("N2"), font));
                        dataTable.AddCell(new Phrase(
                            (totalValue > 0 ? (otherGroupsValue / totalValue * 100).ToString("N1") : "0") + "%", 
                            font));
                    }
                    
                    // Итог
                    dataTable.AddCell(new PdfPCell(new Phrase("Итого", boldFont)) 
                        { Colspan = 1, HorizontalAlignment = Element.ALIGN_RIGHT });
                    dataTable.AddCell(new PdfPCell(new Phrase(totalValue.ToString("N2"), boldFont)));
                    dataTable.AddCell(new PdfPCell(new Phrase("100%", boldFont)));
                    
                    document.Add(dataTable);
                    document.Add(Chunk.Newline);
                }
                
                // Статистика
                document.Add(new Paragraph("Статистика:", boldFont));
                
                var statsTable = new PdfPTable(4)
                {
                    WidthPercentage = 100,
                    SpacingAfter = 10f
                };
                
                AddStatCell(statsTable, "Групп данных", chartData.Count.ToString(), font);
                AddStatCell(statsTable, "Максимум", maxValue.ToString("N2"), font);
                AddStatCell(statsTable, "Минимум", minValue.ToString("N2"), font);
                AddStatCell(statsTable, "Среднее", (chartData.Count > 0 ? (totalValue / chartData.Count).ToString("N2") : "0"), font);
                
                document.Add(statsTable);
                
                document.Close();
                return ms.ToArray();
            }
        }
        
        public byte[] GenerateDetailedReportPdf(
            string title,
            string tableName,
            string timestamp,
            int recordCount,
            List<DatabaseService.ColumnDefinition> columns,
            List<Dictionary<string, object>> data)
        {
            using (var ms = new MemoryStream())
            {
                // Создаем документ A4 с отступами
                var document = new Document(PageSize.A4.Rotate(), 15, 15, 15, 15); // Альбомная ориентация
                var writer = PdfWriter.GetInstance(document, ms);
                
                document.Open();
                
                // Шрифт с поддержкой русского языка
                var baseFont = BaseFont.CreateFont(
                    "c:\\windows\\fonts\\arial.ttf", 
                    BaseFont.IDENTITY_H, 
                    BaseFont.EMBEDDED);
                var font = new Font(baseFont, 8);
                var boldFont = new Font(baseFont, 10, Font.BOLD);
                var titleFont = new Font(baseFont, 14, Font.BOLD);
                var headerFont = new Font(baseFont, 9, Font.BOLD);
                
                // Заголовок отчета
                document.Add(new Paragraph(title, titleFont));
                
                // Информация о таблице
                var infoTable = new PdfPTable(3);
                infoTable.WidthPercentage = 100;
                infoTable.SetWidths(new float[] { 1f, 3f, 1f });
                
                AddInfoCell(infoTable, "Таблица:", tableName, font);
                AddInfoCell(infoTable, "Записей:", recordCount.ToString(), font);
                AddInfoCell(infoTable, "Сформирован:", timestamp, font);
                
                document.Add(infoTable);
                document.Add(Chunk.Newline);
                
                // Создаем таблицу для данных
                var dataTable = new PdfPTable(columns.Count)
                {
                    WidthPercentage = 100,
                    SpacingBefore = 10f,
                    SpacingAfter = 10f
                };
                
                // Установка относительных ширин столбцов
                var columnWidths = new float[columns.Count];
                for (int i = 0; i < columns.Count; i++)
                {
                    columnWidths[i] = 2f; // Базовая ширина
                }
                dataTable.SetWidths(columnWidths);
                
                // Заголовки столбцов
                foreach (var column in columns)
                {
                    var cell = new PdfPCell(new Phrase(column.Name, headerFont))
                    {
                        BackgroundColor = new BaseColor(240, 240, 240),
                        HorizontalAlignment = Element.ALIGN_CENTER,
                        Padding = 5
                    };
                    dataTable.AddCell(cell);
                }
                
                // Данные таблицы
                foreach (var row in data)
                {
                    foreach (var column in columns)
                    {
                        var value = row.ContainsKey(column.Name) 
                            ? row[column.Name]?.ToString() ?? string.Empty
                            : string.Empty;
                        
                        var cell = new PdfPCell(new Phrase(value, font))
                        {
                            Padding = 4,
                            PaddingTop = 3,
                            PaddingBottom = 3
                        };
                        
                        // Выравнивание для числовых столбцов
                        if (column.IsNumeric)
                        {
                            cell.HorizontalAlignment = Element.ALIGN_RIGHT;
                        }
                        
                        dataTable.AddCell(cell);
                    }
                }
                
                document.Add(dataTable);
                
                document.Close();
                return ms.ToArray();
            }
        }
        
        public class ChartDataPdf
        {
            public string Label { get; set; }
            public decimal Value { get; set; }
        }
    }
}