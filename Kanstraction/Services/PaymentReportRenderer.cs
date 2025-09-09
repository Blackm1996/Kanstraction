using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace Kanstraction.Services
{
    public sealed class PaymentReportRenderer
    {
        // ----------- DTOs -----------
        public sealed class SubStageRow
        {
            public string StageName { get; set; } = "";
            public string SubStageName { get; set; } = "";
            public decimal LaborCost { get; set; }
        }

        public sealed class StageGroup
        {
            public string StageName { get; set; } = "";
            public List<SubStageRow> Items { get; set; } = new();
            public decimal StageSubtotal { get; set; }
        }

        public sealed class BuildingGroup
        {
            public string BuildingCode { get; set; } = "";
            // We still receive items grouped by stage, but render them flat.
            public List<StageGroup> Stages { get; set; } = new();
            public decimal BuildingSubtotal { get; set; }
        }

        public sealed class ReportData
        {
            public string ProjectName { get; set; } = "";
            public DateTime GeneratedAt { get; set; } = DateTime.Now;
            public List<BuildingGroup> Buildings { get; set; } = new();
            public decimal GrandTotal { get; set; }
        }

        // ----------- Public entry -----------
        public void Render(ReportData data, string filePath)
        {
            var doc = new Document();
            doc.Info.Title = "Payment Resolution Report";

            // Base style
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

            // Header line: Project (left) | Date (right)
            var header = section.AddTable();
            header.AddColumn(Unit.FromCentimeter(10));
            header.AddColumn(Unit.FromCentimeter(6.5));
            var hr = header.AddRow();
            var left = hr.Cells[0].AddParagraph($"{data.ProjectName}");
            left.Format.Font.Bold = true;
            var right = hr.Cells[1].AddParagraph($"Date: {data.GeneratedAt:yyyy-MM-dd HH:mm}");
            right.Format.Alignment = ParagraphAlignment.Right;

            // Small space after header
            var spacerHeader = section.AddParagraph();
            spacerHeader.Format.SpaceAfter = Unit.FromPoint(6);

            // Master table: Item | Labor
            var table = section.AddTable();
            table.Borders.Color = Colors.Gainsboro;
            table.Borders.Width = 0.5;
            table.LeftPadding = 6;
            table.RightPadding = 6;
            table.Format.SpaceAfter = Unit.FromPoint(8);

            var colItem = table.AddColumn(Unit.FromCentimeter(12.5)); // wide text
            var colAmt = table.AddColumn(Unit.FromCentimeter(4.0));  // amount

            foreach (var b in data.Buildings)
            {
                // ===== Building band =====
                var rb = table.AddRow();
                rb.TopPadding = 4; rb.BottomPadding = 4;
                rb.Cells[0].MergeRight = 1;
                rb.Cells[0].Shading.Color = Colors.Gray;
                var bp = rb.Cells[0].AddParagraph($"{b.BuildingCode}");
                bp.Format.Font.Bold = true;
                bp.Format.Font.Color = Colors.White;
                bp.Format.SpaceBefore = Unit.FromPoint(1);
                bp.Format.SpaceAfter = Unit.FromPoint(1);
                rb.Borders.Bottom.Width = 1; // strong separation

                // ===== Flat list of sub-stages: "Stage: Sub-stage" =====
                bool alt = false;
                foreach (var st in b.Stages)
                {
                    foreach (var item in st.Items)
                    {
                        var rr = table.AddRow();

                        if (alt)
                        {
                            rr.Cells[0].Shading.Color = Colors.LightGray;
                            rr.Cells[1].Shading.Color = Colors.LightGray;
                        }
                        alt = !alt;

                        // Item text: "Stage: Sub-stage"
                        rr.Cells[0].AddParagraph($"{item.StageName}: {item.SubStageName}");

                        var amt = rr.Cells[1].AddParagraph($"{item.LaborCost:0.##}");
                        rr.Cells[1].Format.Alignment = ParagraphAlignment.Right;
                    }
                }

                // ===== Building total =====
                var rTot = table.AddRow();
                rTot.Cells[0].Shading.Color = Colors.LightGray;
                rTot.Cells[1].Shading.Color = Colors.LightGray;

                var tLbl = rTot.Cells[0].AddParagraph("Total");
                tLbl.Format.Font.Bold = true;

                var tAmt = rTot.Cells[1].AddParagraph($"{b.BuildingSubtotal:0.##}");
                rTot.Cells[1].Format.Alignment = ParagraphAlignment.Right;
                rTot.Cells[1].Format.Font.Bold = true;

                rTot.Borders.Top.Width = 0.75; // close the block

                // ===== Exact spacer between buildings =====
                var gap = table.AddRow();
                gap.Borders.Visible = false;
                gap.HeightRule = RowHeightRule.Exactly;
                gap.Height = Unit.FromPoint(8);
            }

            // Grand total
            var gt = section.AddParagraph($"Grand Total: {data.GrandTotal:0.##}");
            gt.Format.Font.Size = 12;
            gt.Format.Font.Bold = true;
            gt.Format.Alignment = ParagraphAlignment.Right;
            gt.Format.SpaceBefore = Unit.FromPoint(8);

            // Footer: page numbers
            var footer = section.Footers.Primary.AddParagraph();
            footer.AddText("Page ");
            footer.AddPageField();
            footer.AddText(" of ");
            footer.AddNumPagesField();
            footer.Format.Alignment = ParagraphAlignment.Center;

            // Render to temp then move to final
            var tmp = filePath + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);

            var renderer = new PdfDocumentRenderer(unicode: true) { Document = doc };
            renderer.RenderDocument();
            renderer.PdfDocument.Save(tmp);

            if (File.Exists(filePath)) File.Delete(filePath);
            File.Move(tmp, filePath);
        }
    }
}
