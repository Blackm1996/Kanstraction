using Kanstraction.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kanstraction.Data;

public static class DbSeeder
{
    public static void Seed(AppDbContext db)
    {
        if (db.Projects.Any()) return; // already seeded

        // ---------- MATERIALS ----------
        var cement = new Material { Name = "Cement", Unit = "bag", PricePerUnit = 6.50m, EffectiveSince = DateTime.Today.AddMonths(-4), IsActive = true };
        var sand = new Material { Name = "Sand", Unit = "m³", PricePerUnit = 12.0m, EffectiveSince = DateTime.Today.AddMonths(-3), IsActive = true };
        var gravel = new Material { Name = "Gravel", Unit = "m³", PricePerUnit = 15.0m, EffectiveSince = DateTime.Today.AddMonths(-3), IsActive = true };
        var rebar = new Material { Name = "Rebar", Unit = "kg", PricePerUnit = 1.20m, EffectiveSince = DateTime.Today.AddMonths(-5), IsActive = true };
        var blocks = new Material { Name = "Blocks", Unit = "pcs", PricePerUnit = 0.90m, EffectiveSince = DateTime.Today.AddMonths(-2), IsActive = true };
        var paint = new Material { Name = "Paint", Unit = "gal", PricePerUnit = 18.0m, EffectiveSince = DateTime.Today.AddMonths(-1), IsActive = true };
        var wiring = new Material { Name = "Wiring", Unit = "m", PricePerUnit = 0.35m, EffectiveSince = DateTime.Today.AddMonths(-6), IsActive = true };
        var pipes = new Material { Name = "Pipes", Unit = "m", PricePerUnit = 0.80m, EffectiveSince = DateTime.Today.AddMonths(-6), IsActive = true };
        var plaster = new Material { Name = "Plaster", Unit = "bag", PricePerUnit = 7.20m, EffectiveSince = DateTime.Today.AddMonths(-2), IsActive = true };

        db.Materials.AddRange(cement, sand, gravel, rebar, blocks, paint, wiring, pipes, plaster);

        // Optional price history to exercise the UI
        db.MaterialPriceHistory.AddRange(
            new MaterialPriceHistory { Material = cement, PricePerUnit = 6.20m, StartDate = DateTime.Today.AddMonths(-4), EndDate = null },
            new MaterialPriceHistory { Material = paint, PricePerUnit = 16.00m, StartDate = DateTime.Today.AddMonths(-7), EndDate = DateTime.Today.AddMonths(-1) },
            new MaterialPriceHistory { Material = paint, PricePerUnit = 18.00m, StartDate = DateTime.Today.AddMonths(-1), EndDate = null }
        );

        db.SaveChanges();

        // ---------- STAGE PRESETS (+ sub-stages + material-usage presets) ----------
        var foundation = MakeStagePreset("Foundation",
            (1, "Excavation", 800m, new[] { (sand, 5m), (gravel, 3m) }),
            (2, "Rebar", 1200m, new[] { (rebar, 500m) }),
            (3, "Pouring", 1500m, new[] { (cement, 60m), (sand, 10m), (gravel, 10m) })
        );

        var structure = MakeStagePreset("Structure",
            (1, "Columns", 1400m, new[] { (rebar, 600m), (cement, 40m) }),
            (2, "Beams", 1200m, new[] { (rebar, 400m), (cement, 30m) }),
            (3, "Slab", 1300m, new[] { (rebar, 300m), (cement, 35m) })
        );

        var walls = MakeStagePreset("Walls",
            (1, "Blockwork", 900m, new[] { (blocks, 1000m), (cement, 20m), (sand, 8m) }),
            (2, "Plastering", 700m, new[] { (plaster, 30m) })
        );

        var mep = MakeStagePreset("MEP",
            (1, "Plumbing Rough-in", 800m, new[] { (pipes, 120m) }),
            (2, "Electrical Rough-in", 900m, new[] { (wiring, 300m) })
        );

        var finishing = MakeStagePreset("Finishing",
            (1, "Primer", 500m, new[] { (paint, 5m) }),
            (2, "Final Paint", 700m, new[] { (paint, 10m) })
        );

        db.StagePresets.AddRange(foundation.Preset, structure.Preset, walls.Preset, mep.Preset, finishing.Preset);
        db.SaveChanges();

        // ---------- BUILDING TYPES + assignments ----------
        var villa = new BuildingType { Name = "Villa", IsActive = true };
        var duplex = new BuildingType { Name = "Duplex", IsActive = true };
        var apartment = new BuildingType { Name = "Apartment", IsActive = true };
        db.BuildingTypes.AddRange(villa, duplex, apartment);
        db.SaveChanges();

        void Assign(BuildingType t, StagePreset p, int order) =>
            db.BuildingTypeStagePresets.Add(new BuildingTypeStagePreset { BuildingType = t, StagePreset = p, OrderIndex = order });

        // Villa: full pipeline
        Assign(villa, foundation.Preset, 1);
        Assign(villa, structure.Preset, 2);
        Assign(villa, walls.Preset, 3);
        Assign(villa, mep.Preset, 4);
        Assign(villa, finishing.Preset, 5);

        // Duplex: no MEP in presets (for variety)
        Assign(duplex, foundation.Preset, 1);
        Assign(duplex, structure.Preset, 2);
        Assign(duplex, walls.Preset, 3);
        Assign(duplex, finishing.Preset, 4);

        // Apartment: no finishing (for variety)
        Assign(apartment, foundation.Preset, 1);
        Assign(apartment, structure.Preset, 2);
        Assign(apartment, mep.Preset, 3);

        db.SaveChanges();

        // ---------- PROJECTS ----------
        var p1 = new Project { Name = "Hills Compound", StartDate = DateTime.Today.AddMonths(-5) };
        var p2 = new Project { Name = "Seaside Villas", StartDate = DateTime.Today.AddMonths(-3) };
        var p3 = new Project { Name = "Urban Towers", StartDate = DateTime.Today.AddMonths(-6) };
        db.Projects.AddRange(p1, p2, p3);
        db.SaveChanges();

        // ---------- BUILDINGS (copy presets → real stages/sub-stages/usages) ----------
        // p1: showcase NotStarted, Ongoing (stage2/sub1), Finished
        AddBuildingFromType(db, p1, villa, "V-101", make: NotStarted);
        AddBuildingFromType(db, p1, villa, "V-102", make: b => OngoingAt(db, b, stageOrder: 2, subOrder: 1));
        AddBuildingFromType(db, p1, duplex, "D-201", make: FinishedAll);

        // p2: showcase Stopped (after some progress), and Ongoing (stage1/sub2)
        AddBuildingFromType(db, p2, duplex, "D-202", make: b => ProgressSomeThenStop(db, b));
        AddBuildingFromType(db, p2, apartment, "A-301", make: b => OngoingAt(db, b, stageOrder: 1, subOrder: 2));

        // p3: showcase Paid (everything paid)
        AddBuildingFromType(db, p3, apartment, "A-302", make: PaidAll);

        db.SaveChanges();

        // ---------------- local helpers ----------------

        static (StagePreset Preset, List<SubStagePreset> Subs) MakeStagePreset(
            string name,
            params (int order, string title, decimal labor, (Material mat, decimal qty)[] mats)[] subs)
        {
            var preset = new StagePreset { Name = name, IsActive = true };
            var list = new List<SubStagePreset>();

            foreach (var s in subs.OrderBy(x => x.order))
            {
                var sp = new SubStagePreset
                {
                    StagePreset = preset,
                    Name = s.title,
                    OrderIndex = s.order,
                    LaborCost = s.labor
                };
                list.Add(sp);

                // material usage presets for this sub
                foreach (var (mat, qty) in s.mats)
                {
                    var mup = new MaterialUsagePreset
                    {
                        SubStagePreset = sp,
                        Material = mat,
                        Qty = qty
                    };
                    // EF will pick it up since it's attached to SubStagePreset
                }
            }
            return (preset, list);
        }

        static void AddBuildingFromType(AppDbContext ctx, Project proj, BuildingType type, string code, Action<Building> make)
        {
            // create building
            var b = new Building
            {
                Project = proj,
                BuildingType = type,
                Code = code,
                Status = WorkStatus.NotStarted
            };
            ctx.Buildings.Add(b);
            ctx.SaveChanges();

            // copy assigned stage presets in order
            var presetIds = ctx.BuildingTypeStagePresets
                .Where(x => x.BuildingTypeId == type.Id)
                .OrderBy(x => x.OrderIndex)
                .Select(x => x.StagePresetId)
                .ToList();

            int order = 1;
            foreach (var pid in presetIds)
            {
                var sp = ctx.StagePresets
                    .Include(s => s.SubStages)
                    .First(s => s.Id == pid);

                var stage = new Stage
                {
                    Building = b,
                    Name = sp.Name,
                    OrderIndex = order++,
                    Status = WorkStatus.NotStarted
                };
                ctx.Stages.Add(stage);
                ctx.SaveChanges();

                foreach (var ssp in sp.SubStages.OrderBy(x => x.OrderIndex))
                {
                    var sub = new SubStage
                    {
                        Stage = stage,
                        Name = ssp.Name,
                        OrderIndex = ssp.OrderIndex,
                        Status = WorkStatus.NotStarted,
                        LaborCost = ssp.LaborCost
                    };
                    ctx.SubStages.Add(sub);
                    ctx.SaveChanges();

                    // copy material usage presets
                    var muPresets = ctx.Set<MaterialUsagePreset>()
                        .Where(mup => mup.SubStagePresetId == ssp.Id)
                        .Include(mup => mup.Material)
                        .ToList();

                    foreach (var mup in muPresets)
                    {
                        var mu = new MaterialUsage
                        {
                            SubStage = sub,
                            Material = mup.Material,
                            Qty = mup.Qty,
                            UsageDate = DateTime.Today
                        };
                        ctx.MaterialUsages.Add(mu);
                    }
                    ctx.SaveChanges();
                }
            }

            // apply desired final state
            make(b);

            ctx.SaveChanges();
        }

        // simple “do-nothing” state
        static void NotStarted(Building b) { /* leaves all as NotStarted */ }

        // mark everything Finished
        static void FinishedAll(Building b)
        {
            foreach (var s in b.Stages.OrderBy(x => x.OrderIndex))
            {
                foreach (var ss in s.SubStages.OrderBy(x => x.OrderIndex))
                    ss.Status = WorkStatus.Finished;
                s.Status = WorkStatus.Finished;
            }
            b.Status = WorkStatus.Finished;
        }

        // mark everything Paid
        static void PaidAll(Building b)
        {
            foreach (var s in b.Stages.OrderBy(x => x.OrderIndex))
            {
                foreach (var ss in s.SubStages.OrderBy(x => x.OrderIndex))
                    ss.Status = WorkStatus.Paid;
                s.Status = WorkStatus.Paid;
            }
            b.Status = WorkStatus.Paid;
        }

        // finish earlier stages, in target stage finish previous sub-stages, start target sub-stage
        static void OngoingAt(AppDbContext ctx, Building b, int stageOrder, int subOrder)
        {
            foreach (var s in b.Stages.Where(s => s.OrderIndex < stageOrder).OrderBy(s => s.OrderIndex))
            {
                foreach (var ss in s.SubStages.OrderBy(x => x.OrderIndex))
                    ss.Status = WorkStatus.Finished;
                s.Status = WorkStatus.Finished;
            }

            var targetStage = b.Stages.FirstOrDefault(s => s.OrderIndex == stageOrder);
            if (targetStage == null) return;

            foreach (var ss in targetStage.SubStages.Where(x => x.OrderIndex < subOrder).OrderBy(x => x.OrderIndex))
                ss.Status = WorkStatus.Finished;

            var targetSub = targetStage.SubStages.FirstOrDefault(x => x.OrderIndex == subOrder);
            if (targetSub != null)
            {
                targetSub.Status = WorkStatus.Ongoing;
                targetStage.Status = WorkStatus.Ongoing;
                b.Status = WorkStatus.Ongoing;
            }
        }

        // progress a bit then stop everything not finished/paid
        static void ProgressSomeThenStop(AppDbContext ctx, Building b)
        {
            var first = b.Stages.OrderBy(s => s.OrderIndex).FirstOrDefault();
            if (first != null)
            {
                foreach (var ss in first.SubStages.OrderBy(x => x.OrderIndex))
                    ss.Status = WorkStatus.Finished;
                first.Status = WorkStatus.Finished;
            }

            var second = b.Stages.OrderBy(s => s.OrderIndex).Skip(1).FirstOrDefault();
            if (second != null)
            {
                var s1 = second.SubStages.OrderBy(x => x.OrderIndex).FirstOrDefault();
                if (s1 != null)
                {
                    s1.Status = WorkStatus.Ongoing;
                    second.Status = WorkStatus.Ongoing;
                    b.Status = WorkStatus.Ongoing;
                }
            }

            // now stop building
            b.Status = WorkStatus.Stopped;
            foreach (var s in b.Stages)
            {
                if (s.Status != WorkStatus.Finished && s.Status != WorkStatus.Paid)
                    s.Status = WorkStatus.Stopped;

                foreach (var ss in s.SubStages)
                    if (ss.Status != WorkStatus.Finished && ss.Status != WorkStatus.Paid)
                        ss.Status = WorkStatus.Stopped;
            }
        }
    }
}
