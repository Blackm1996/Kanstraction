using Kanstraction.Entities;

namespace Kanstraction.Data;

public static class DbSeeder
{
    public static void Seed(AppDbContext db)
    {
        // Prevent duplicate seed
        if (db.Projects.Any()) return;

        // --- Materials ---
        var cement = new Material { Name = "Cement", Unit = "kg", PricePerUnit = 5.00m, EffectiveSince = DateTime.Today.AddMonths(-2), IsActive = true };
        var rebar = new Material { Name = "Rebar Steel", Unit = "kg", PricePerUnit = 1.30m, EffectiveSince = DateTime.Today.AddMonths(-1), IsActive = true };
        var sand = new Material { Name = "Sand", Unit = "m³", PricePerUnit = 18.00m, EffectiveSince = DateTime.Today.AddMonths(-3), IsActive = true };
        var gravel = new Material { Name = "Gravel", Unit = "m³", PricePerUnit = 22.00m, EffectiveSince = DateTime.Today.AddMonths(-3), IsActive = true };
        var bricks = new Material { Name = "Bricks", Unit = "piece", PricePerUnit = 0.35m, EffectiveSince = DateTime.Today.AddMonths(-4), IsActive = true };
        var paint = new Material { Name = "Interior Paint", Unit = "L", PricePerUnit = 9.50m, EffectiveSince = DateTime.Today.AddMonths(-2), IsActive = true };
        var wire = new Material { Name = "Electrical Wire", Unit = "m", PricePerUnit = 0.80m, EffectiveSince = DateTime.Today.AddMonths(-1), IsActive = true };
        var pvc = new Material { Name = "PVC Pipe 2\"", Unit = "m", PricePerUnit = 1.40m, EffectiveSince = DateTime.Today.AddMonths(-1), IsActive = true };

        db.Materials.AddRange(cement, rebar, sand, gravel, bricks, paint, wire, pvc);
        db.SaveChanges();

        // --- Project & Building Types ---
        var project = new Project
        {
            Name = "Showcase Project",
            StartDate = DateTime.Today.AddDays(-45)
        };
        db.Projects.Add(project);
        db.SaveChanges();

        var villa = new BuildingType { Name = "Villa" };
        var duplex = new BuildingType { Name = "Duplex" };
        db.BuildingTypes.AddRange(villa, duplex);
        db.SaveChanges();

        // Helper local functions to add stages/sub-stages quickly
        Stage MkStage(int buildingId, string name, int idx, WorkStatus status) =>
            new Stage
            {
                BuildingId = buildingId,
                Name = name,
                OrderIndex = idx,
                Status = status,
                StartDate = status is WorkStatus.Ongoing or WorkStatus.Finished or WorkStatus.Paid ? DateTime.Today.AddDays(-20 + idx * 2) : null,
                EndDate = status is WorkStatus.Finished or WorkStatus.Paid ? DateTime.Today.AddDays(-5 + idx) : null
            };

        SubStage MkSub(int stageId, string name, int idx, WorkStatus status, decimal labor) =>
            new SubStage
            {
                StageId = stageId,
                Name = name,
                OrderIndex = idx,
                Status = status,
                LaborCost = labor,
                StartDate = status is WorkStatus.Ongoing or WorkStatus.Finished or WorkStatus.Paid ? DateTime.Today.AddDays(-15 + idx) : null,
                EndDate = status is WorkStatus.Finished or WorkStatus.Paid ? DateTime.Today.AddDays(-8 + idx) : null
            };

        // =========================
        // Building 1: NOT STARTED
        // =========================
        var b1 = new Building
        {
            ProjectId = project.Id,
            BuildingTypeId = villa.Id,
            Code = "B-NS-01",
            Status = WorkStatus.NotStarted
        };
        db.Buildings.Add(b1); db.SaveChanges();

        var b1_s1 = MkStage(b1.Id, "Foundation", 1, WorkStatus.NotStarted);
        var b1_s2 = MkStage(b1.Id, "Elevation", 2, WorkStatus.NotStarted);
        var b1_s3 = MkStage(b1.Id, "Finishing", 3, WorkStatus.NotStarted);
        db.Stages.AddRange(b1_s1, b1_s2, b1_s3); db.SaveChanges();

        db.SubStages.AddRange(
            MkSub(b1_s1.Id, "Excavation", 1, WorkStatus.NotStarted, 0),
            MkSub(b1_s1.Id, "Rebar", 2, WorkStatus.NotStarted, 0),
            MkSub(b1_s1.Id, "Concrete", 3, WorkStatus.NotStarted, 0)
        );
        db.SaveChanges();

        // ======================
        // Building 2: ONGOING
        // ======================
        var b2 = new Building
        {
            ProjectId = project.Id,
            BuildingTypeId = villa.Id,
            Code = "B-OG-02",
            Status = WorkStatus.Ongoing
        };
        db.Buildings.Add(b2); db.SaveChanges();

        var b2_s1 = MkStage(b2.Id, "Foundation", 1, WorkStatus.Finished);
        var b2_s2 = MkStage(b2.Id, "Elevation", 2, WorkStatus.Ongoing);
        var b2_s3 = MkStage(b2.Id, "Finishing", 3, WorkStatus.NotStarted);
        db.Stages.AddRange(b2_s1, b2_s2, b2_s3); db.SaveChanges();

        db.SubStages.AddRange(
            // Foundation finished
            MkSub(b2_s1.Id, "Excavation", 1, WorkStatus.Finished, 2000),
            MkSub(b2_s1.Id, "Rebar", 2, WorkStatus.Finished, 1200),
            MkSub(b2_s1.Id, "Concrete", 3, WorkStatus.Finished, 1800),

            // Elevation ongoing
            MkSub(b2_s2.Id, "Walls", 1, WorkStatus.Ongoing, 800),
            MkSub(b2_s2.Id, "Plaster", 2, WorkStatus.NotStarted, 0)
        );
        db.SaveChanges();

        // Add a few material usages under an ongoing sub-stage
        db.MaterialUsages.AddRange(
            new MaterialUsage { SubStageId = db.SubStages.First(x => x.StageId == b2_s2.Id && x.Name == "Walls").Id, MaterialId = bricks.Id, Qty = 500, UsageDate = DateTime.Today.AddDays(-2) },
            new MaterialUsage { SubStageId = db.SubStages.First(x => x.StageId == b2_s2.Id && x.Name == "Walls").Id, MaterialId = cement.Id, Qty = 50, UsageDate = DateTime.Today.AddDays(-1) },
            new MaterialUsage { SubStageId = db.SubStages.First(x => x.StageId == b2_s2.Id && x.Name == "Walls").Id, MaterialId = sand.Id, Qty = 2, UsageDate = DateTime.Today }
        );
        db.SaveChanges();

        // =======================
        // Building 3: FINISHED
        // =======================
        var b3 = new Building
        {
            ProjectId = project.Id,
            BuildingTypeId = duplex.Id,
            Code = "B-FN-03",
            Status = WorkStatus.Finished
        };
        db.Buildings.Add(b3); db.SaveChanges();

        var b3_s1 = MkStage(b3.Id, "Foundation", 1, WorkStatus.Finished);
        var b3_s2 = MkStage(b3.Id, "Elevation", 2, WorkStatus.Finished);
        var b3_s3 = MkStage(b3.Id, "Finishing", 3, WorkStatus.Finished);
        db.Stages.AddRange(b3_s1, b3_s2, b3_s3); db.SaveChanges();

        db.SubStages.AddRange(
            MkSub(b3_s1.Id, "Excavation", 1, WorkStatus.Finished, 2200),
            MkSub(b3_s1.Id, "Rebar", 2, WorkStatus.Finished, 1300),
            MkSub(b3_s1.Id, "Concrete", 3, WorkStatus.Finished, 1900),

            MkSub(b3_s2.Id, "Walls", 1, WorkStatus.Finished, 900),
            MkSub(b3_s2.Id, "Plaster", 2, WorkStatus.Finished, 700),

            MkSub(b3_s3.Id, "Paint", 1, WorkStatus.Finished, 600),
            MkSub(b3_s3.Id, "Fixtures", 2, WorkStatus.Finished, 850)
        );
        db.SaveChanges();

        // ===================
        // Building 4: PAID
        // ===================
        var b4 = new Building
        {
            ProjectId = project.Id,
            BuildingTypeId = duplex.Id,
            Code = "B-PD-04",
            Status = WorkStatus.Paid
        };
        db.Buildings.Add(b4); db.SaveChanges();

        var b4_s1 = MkStage(b4.Id, "Foundation", 1, WorkStatus.Paid);
        var b4_s2 = MkStage(b4.Id, "Elevation", 2, WorkStatus.Paid);
        var b4_s3 = MkStage(b4.Id, "Finishing", 3, WorkStatus.Paid);
        db.Stages.AddRange(b4_s1, b4_s2, b4_s3); db.SaveChanges();

        db.SubStages.AddRange(
            MkSub(b4_s1.Id, "Excavation", 1, WorkStatus.Paid, 2300),
            MkSub(b4_s1.Id, "Rebar", 2, WorkStatus.Paid, 1400),
            MkSub(b4_s1.Id, "Concrete", 3, WorkStatus.Paid, 2000),

            MkSub(b4_s2.Id, "Walls", 1, WorkStatus.Paid, 950),
            MkSub(b4_s2.Id, "Plaster", 2, WorkStatus.Paid, 750),

            MkSub(b4_s3.Id, "Paint", 1, WorkStatus.Paid, 650),
            MkSub(b4_s3.Id, "Fixtures", 2, WorkStatus.Paid, 900)
        );
        db.SaveChanges();

        // =====================
        // Building 5: STOPPED
        // =====================
        var b5 = new Building
        {
            ProjectId = project.Id,
            BuildingTypeId = villa.Id,
            Code = "B-ST-05",
            Status = WorkStatus.Stopped
        };
        db.Buildings.Add(b5); db.SaveChanges();

        var b5_s1 = MkStage(b5.Id, "Foundation", 1, WorkStatus.Finished); // already finished before stop
        var b5_s2 = MkStage(b5.Id, "Elevation", 2, WorkStatus.Stopped);  // stopped mid-way
        var b5_s3 = MkStage(b5.Id, "Finishing", 3, WorkStatus.NotStarted); // never started
        db.Stages.AddRange(b5_s1, b5_s2, b5_s3); db.SaveChanges();

        db.SubStages.AddRange(
            // Finished stage
            MkSub(b5_s1.Id, "Excavation", 1, WorkStatus.Finished, 2100),
            MkSub(b5_s1.Id, "Rebar", 2, WorkStatus.Finished, 1250),
            MkSub(b5_s1.Id, "Concrete", 3, WorkStatus.Finished, 1850),

            // Stopped stage: some items stopped, some finished earlier
            MkSub(b5_s2.Id, "Walls", 1, WorkStatus.Stopped, 500),
            MkSub(b5_s2.Id, "Plaster", 2, WorkStatus.Stopped, 0),

            // Not started stage: sub-stages not started
            MkSub(b5_s3.Id, "Paint", 1, WorkStatus.NotStarted, 0),
            MkSub(b5_s3.Id, "Fixtures", 2, WorkStatus.NotStarted, 0)
        );
        db.SaveChanges();

        // Optional: a bit of price history for one material
        db.MaterialPriceHistory.Add(new MaterialPriceHistory
        {
            MaterialId = cement.Id,
            PricePerUnit = 4.60m,
            StartDate = DateTime.Today.AddMonths(-6),
            EndDate = DateTime.Today.AddMonths(-2)
        });
        db.SaveChanges();
    }
}
