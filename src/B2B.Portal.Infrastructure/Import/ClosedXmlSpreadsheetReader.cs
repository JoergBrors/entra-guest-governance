using B2B.Portal.Application.Ports;
using ClosedXML.Excel;

namespace B2B.Portal.Infrastructure.Import;

/// <summary>
/// ClosedXML-Implementierung von ISpreadsheetReader (Excel-Gäste-Import, siehe
/// GuestImportService). Jede Methode öffnet ein eigenes XLWorkbook und setzt die
/// Stream-Position vorher zurück — der aufrufende Endpoint liest denselben hochgeladenen
/// Stream für Inspect/Preview/Commit ggf. mehrfach.
/// </summary>
public sealed class ClosedXmlSpreadsheetReader : ISpreadsheetReader
{
    public IReadOnlyList<string> GetSheetNames(Stream xlsxStream)
    {
        xlsxStream.Position = 0;
        using var workbook = new XLWorkbook(xlsxStream);
        return workbook.Worksheets.Select(w => w.Name).ToList();
    }

    public IReadOnlyList<string> ReadHeaderRow(Stream xlsxStream, string sheetName, int headerRowIndex, int dataStartColumnIndex)
    {
        xlsxStream.Position = 0;
        using var workbook = new XLWorkbook(xlsxStream);
        var sheet = ResolveSheet(workbook, sheetName);

        var headerRow = sheet.Row(headerRowIndex);
        var headers = new List<string>();
        var col = dataStartColumnIndex;
        while (!headerRow.Cell(col).IsEmpty())
        {
            headers.Add(headerRow.Cell(col).GetString());
            col++;
        }
        return headers;
    }

    public IReadOnlyList<IReadOnlyDictionary<int, string>> ReadDataRows(
        Stream xlsxStream, string sheetName, int headerRowIndex, int dataStartColumnIndex)
    {
        xlsxStream.Position = 0;
        using var workbook = new XLWorkbook(xlsxStream);
        var sheet = ResolveSheet(workbook, sheetName);

        var rows = new List<IReadOnlyDictionary<int, string>>();
        var rowIndex = headerRowIndex + 1;
        var row = sheet.Row(rowIndex);
        while (!row.Cell(dataStartColumnIndex).IsEmpty())
        {
            var values = new Dictionary<int, string>();
            var col = dataStartColumnIndex;
            var offset = 0;
            while (!sheet.Row(headerRowIndex).Cell(col).IsEmpty())
            {
                values[offset] = row.Cell(col).GetString();
                col++;
                offset++;
            }
            rows.Add(values);

            rowIndex++;
            row = sheet.Row(rowIndex);
        }
        return rows;
    }

    private static IXLWorksheet ResolveSheet(XLWorkbook workbook, string sheetName) =>
        workbook.Worksheets.FirstOrDefault(w => string.Equals(w.Name, sheetName, StringComparison.OrdinalIgnoreCase))
            ?? workbook.Worksheets.First();
}
