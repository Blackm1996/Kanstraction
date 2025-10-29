using Kanstraction.Application.Reporting;
using Kanstraction.Shared.Localization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using System.IO;

namespace Kanstraction.Infrastructure.Services;

public sealed class PaymentReportRenderer : IPaymentReportRenderer
{
    private readonly IPaymentReportLocalizer _localizer;

    public PaymentReportRenderer(IPaymentReportLocalizer localizer)
    {
        _localizer = localizer;
    }

    public void Render(PaymentReportData data, string filePath)
    {
        var doc = new Document();
        doc.Info.Title = _localizer.Title;

        var normal = doc.Styles["Normal"];
        normal.Font.Name = "Segoe UI";
        normal.Font.Size = 10;

        var h1 = doc.Styles.AddStyle("H1", "Normal");
        h1.Font.Size = 16;
        h1.Font.Bold = true;

        var section = doc.AddSection();
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.RightMargin = Unit.FromCentimeter(1.5);

        var header = section.AddTable();
        header.AddColumn(Unit.FromCentimeter(10));
        header.AddColumn(Unit.FromCentimeter(6.5));
        var hr = header.AddRow();
        var left = hr.Cells[0].AddParagraph(data.ProjectName);
        left.Format.Font.Bold = true;
        var right = hr.Cells[1].AddParagraph($"Date: {data.GeneratedAt:yyyy-MM-dd HH:mm}");
        right.Format.Alignment = ParagraphAlignment.Right;

        var spacerHeader = section.AddParagraph();
        spacerHeader.Format.SpaceAfter = Unit.FromPoint(6);

        var table = section.AddTable();
        table.Borders.Color = Colors.Gainsboro;
        table.Borders.Width = 0.5;
        table.LeftPadding = 6;
        table.RightPadding = 6;
        table.Format.SpaceAfter = Unit.FromPoint(8);

        table.AddColumn(Unit.FromCentimeter(12.5));
        table.AddColumn(Unit.FromCentimeter(4.0));

        foreach (var building in data.Buildings)
        {
            var buildingRow = table.AddRow();
            buildingRow.TopPadding = 4;
            buildingRow.BottomPadding = 4;
            buildingRow.Cells[0].MergeRight = 1;
            buildingRow.Cells[0].Shading.Color = Colors.Gray;
            var buildingParagraph = buildingRow.Cells[0].AddParagraph(building.BuildingCode);
            buildingParagraph.Format.Font.Bold = true;
            buildingParagraph.Format.Font.Color = Colors.White;
            buildingParagraph.Format.SpaceBefore = Unit.FromPoint(1);
            buildingParagraph.Format.SpaceAfter = Unit.FromPoint(1);
            buildingRow.Borders.Bottom.Width = 1;

            var alternate = false;
            foreach (var stage in building.Stages)
            {
                foreach (var item in stage.Items)
                {
                    var detailRow = table.AddRow();
                    if (alternate)
                    {
                        detailRow.Cells[0].Shading.Color = Colors.LightGray;
                        detailRow.Cells[1].Shading.Color = Colors.LightGray;
                    }
                    alternate = !alternate;

                    detailRow.Cells[0].AddParagraph($"{item.StageName}: {item.SubStageName}");
                    var amount = detailRow.Cells[1].AddParagraph($"{item.LaborCost:0.##}");
                    amount.Format.Alignment = ParagraphAlignment.Right;
                }
            }

            var totalRow = table.AddRow();
            totalRow.Cells[0].Shading.Color = Colors.LightGray;
            totalRow.Cells[1].Shading.Color = Colors.LightGray;
            var totalLabel = totalRow.Cells[0].AddParagraph("Total");
            totalLabel.Format.Font.Bold = true;
            var totalAmount = totalRow.Cells[1].AddParagraph($"{building.BuildingSubtotal:0.##}");
            totalRow.Cells[1].Format.Alignment = ParagraphAlignment.Right;
            totalRow.Cells[1].Format.Font.Bold = true;
            totalRow.Borders.Top.Width = 0.75;

            var gap = table.AddRow();
            gap.Borders.Visible = false;
            gap.HeightRule = RowHeightRule.Exactly;
            gap.Height = Unit.FromPoint(8);
        }

        var grandTotal = section.AddParagraph($"Grand Total: {data.GrandTotal:0.##}");
        grandTotal.Format.Font.Size = 12;
        grandTotal.Format.Font.Bold = true;
        grandTotal.Format.Alignment = ParagraphAlignment.Right;
        grandTotal.Format.SpaceBefore = Unit.FromPoint(8);

        var footer = section.Footers.Primary.AddParagraph();
        footer.AddText(_localizer.FooterPage);
        footer.AddPageField();
        footer.AddText(_localizer.FooterOf);
        footer.AddNumPagesField();

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();
        renderer.PdfDocument.Save(filePath);
    }
}
