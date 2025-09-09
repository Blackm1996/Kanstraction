using System;
using System.Linq;
using System.Collections.Generic;
using Kanstraction.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kanstraction.Data;

public static class DbSeeder
{
    /// <summary>
    /// Seeds presets ONLY:
    /// - Materials (+ a bit of price history)
    /// - StagePresets WITH SubStagePresets and MaterialUsagePresets
    /// - BuildingTypes WITH StagePreset assignments
    /// Safe to call multiple times; skips existing by name.
    /// </summary>
    public static void Seed(AppDbContext db)
    {
        if (db.Materials.Any() && db.StagePresets.Any() && db.BuildingTypes.Any())
            return;

        // ============ MATERIALS ============
        var materials = EnsureMaterials(db);

        // ============ STAGE PRESETS (+ SUBS + MATERIAL USAGES) ============
        EnsureStagePresets(db, materials);

        // ============ BUILDING TYPES ============
        EnsureBuildingTypes(db);
    }

    // ---------------- MATERIALS ----------------
    private static Dictionary<string, Material> EnsureMaterials(AppDbContext db)
    {
        var want = new[]
        {
            new Material { Name = "Cement",  Unit = "bag", PricePerUnit = 6.50m,  EffectiveSince = DateTime.Today.AddMonths(-4), IsActive = true },
            new Material { Name = "Sand",    Unit = "m³",  PricePerUnit = 12.00m, EffectiveSince = DateTime.Today.AddMonths(-3), IsActive = true },
            new Material { Name = "Gravel",  Unit = "m³",  PricePerUnit = 15.00m, EffectiveSince = DateTime.Today.AddMonths(-3), IsActive = true },
            new Material { Name = "Rebar",   Unit = "kg",  PricePerUnit = 1.20m,  EffectiveSince = DateTime.Today.AddMonths(-5), IsActive = true },
            new Material { Name = "Blocks",  Unit = "pcs", PricePerUnit = 0.90m,  EffectiveSince = DateTime.Today.AddMonths(-2), IsActive = true },
            new Material { Name = "Paint",   Unit = "gal", PricePerUnit = 18.00m, EffectiveSince = DateTime.Today.AddMonths(-1), IsActive = true },
            new Material { Name = "Wiring",  Unit = "m",   PricePerUnit = 0.35m,  EffectiveSince = DateTime.Today.AddMonths(-6), IsActive = true },
            new Material { Name = "Pipes",   Unit = "m",   PricePerUnit = 0.80m,  EffectiveSince = DateTime.Today.AddMonths(-6), IsActive = true },
            new Material { Name = "Plaster", Unit = "bag", PricePerUnit = 7.20m,  EffectiveSince = DateTime.Today.AddMonths(-2), IsActive = true },
        };

        foreach (var m in want)
        {
            var exists = db.Materials.FirstOrDefault(x => x.Name == m.Name);
            if (exists == null)
                db.Materials.Add(m);
        }
        db.SaveChanges();

        // Small history sample
        var paint = db.Materials.First(x => x.Name == "Paint");
        if (!db.MaterialPriceHistory.Any(h => h.MaterialId == paint.Id))
        {
            db.MaterialPriceHistory.AddRange(
                new MaterialPriceHistory { MaterialId = paint.Id, PricePerUnit = 16.00m, StartDate = DateTime.Today.AddMonths(-7), EndDate = DateTime.Today.AddMonths(-1) },
                new MaterialPriceHistory { MaterialId = paint.Id, PricePerUnit = 18.00m, StartDate = DateTime.Today.AddMonths(-1), EndDate = null }
            );
            db.SaveChanges();
        }

        return db.Materials.ToDictionary(x => x.Name, x => x);
    }

    // ---------------- STAGE PRESETS ----------------
    private static void EnsureStagePresets(AppDbContext db, Dictionary<string, Material> m)
    {
        // Helper to upsert a StagePreset + its sub-stages + materials
        void UpsertStagePreset(string presetName, (int order, string name, decimal labor, (string mat, decimal qty)[] uses)[] subs)
        {
            var preset = db.StagePresets.FirstOrDefault(p => p.Name == presetName);
            if (preset == null)
            {
                preset = new StagePreset { Name = presetName, IsActive = true };
                db.StagePresets.Add(preset);
                db.SaveChanges(); // need ID
            }

            // Ensure sub-stages
            foreach (var s in subs.OrderBy(x => x.order))
            {
                var sub = db.SubStagePresets
                    .FirstOrDefault(ss => ss.StagePresetId == preset.Id && ss.Name == s.name);

                if (sub == null)
                {
                    sub = new SubStagePreset
                    {
                        StagePresetId = preset.Id,
                        Name = s.name,
                        OrderIndex = s.order,
                        LaborCost = s.labor
                    };
                    db.SubStagePresets.Add(sub);
                    db.SaveChanges();
                }
                else
                {
                    // keep it updated
                    sub.OrderIndex = s.order;
                    sub.LaborCost = s.labor;
                    db.SaveChanges();
                }

                // Ensure material usages for this sub-stage
                foreach (var (matName, qty) in s.uses)
                {
                    if (!m.TryGetValue(matName, out var mat)) continue;

                    var exists = db.MaterialUsagesPreset.FirstOrDefault(mu =>
                        mu.SubStagePresetId == sub.Id && mu.MaterialId == mat.Id);

                    if (exists == null)
                    {
                        db.MaterialUsagesPreset.Add(new MaterialUsagePreset
                        {
                            SubStagePresetId = sub.Id,
                            MaterialId = mat.Id,
                            Qty = qty
                        });
                    }
                    else
                    {
                        exists.Qty = qty; // update qty if changed
                    }
                }
                db.SaveChanges();
            }
        }

        // Foundation
        UpsertStagePreset("Foundation", new[]
        {
            (1, "Excavation", 800m,  new[] { ("Sand", 5m), ("Gravel", 3m) }),
            (2, "Rebar",      1200m, new[] { ("Rebar", 500m) }),
            (3, "Pouring",    1500m, new[] { ("Cement", 60m), ("Sand", 10m), ("Gravel", 10m) })
        });

        // Structure
        UpsertStagePreset("Structure", new[]
        {
            (1, "Columns", 1400m, new[] { ("Rebar", 600m), ("Cement", 40m) }),
            (2, "Beams",   1200m, new[] { ("Rebar", 400m), ("Cement", 30m) }),
            (3, "Slab",    1300m, new[] { ("Rebar", 300m), ("Cement", 35m) })
        });

        // Walls
        UpsertStagePreset("Walls", new[]
        {
            (1, "Blockwork",  900m, new[] { ("Blocks", 1000m), ("Cement", 20m), ("Sand", 8m) }),
            (2, "Plastering", 700m, new[] { ("Plaster", 30m) })
        });

        // MEP
        UpsertStagePreset("MEP", new[]
        {
            (1, "Plumbing Rough-in",   800m, new[] { ("Pipes", 120m) }),
            (2, "Electrical Rough-in", 900m, new[] { ("Wiring", 300m) })
        });

        // Finishing
        UpsertStagePreset("Finishing", new[]
        {
            (1, "Primer",      500m, new[] { ("Paint", 5m) }),
            (2, "Final Paint", 700m, new[] { ("Paint", 10m) })
        });
    }

    // ---------------- BUILDING TYPES ----------------
    private static void EnsureBuildingTypes(AppDbContext db)
    {
        // Ensure types
        BuildingType EnsureType(string name)
        {
            var t = db.BuildingTypes.FirstOrDefault(x => x.Name == name);
            if (t != null) return t;
            t = new BuildingType { Name = name, IsActive = true };
            db.BuildingTypes.Add(t);
            db.SaveChanges();
            return t;
        }

        var villa = EnsureType("Villa");
        var duplex = EnsureType("Duplex");
        var apartment = EnsureType("Apartment");

        // Load presets
        var presets = db.StagePresets.AsNoTracking().ToList();
        StagePreset P(string name) => presets.First(x => x.Name == name);

        // Helper: ensure assignment
        void Assign(BuildingType t, StagePreset p, int order)
        {
            var exists = db.BuildingTypeStagePresets
                .FirstOrDefault(x => x.BuildingTypeId == t.Id && x.StagePresetId == p.Id);
            if (exists == null)
            {
                db.BuildingTypeStagePresets.Add(new BuildingTypeStagePreset
                {
                    BuildingTypeId = t.Id,
                    StagePresetId = p.Id,
                    OrderIndex = order
                });
                db.SaveChanges();
            }
            else
            {
                if (exists.OrderIndex != order)
                {
                    exists.OrderIndex = order;
                    db.SaveChanges();
                }
            }
        }

        // Villa: Foundation → Structure → Walls → MEP → Finishing
        Assign(villa, P("Foundation"), 1);
        Assign(villa, P("Structure"), 2);
        Assign(villa, P("Walls"), 3);
        Assign(villa, P("MEP"), 4);
        Assign(villa, P("Finishing"), 5);

        // Duplex: Foundation → Structure → Walls → Finishing
        Assign(duplex, P("Foundation"), 1);
        Assign(duplex, P("Structure"), 2);
        Assign(duplex, P("Walls"), 3);
        Assign(duplex, P("Finishing"), 4);

        // Apartment: Foundation → Structure → MEP
        Assign(apartment, P("Foundation"), 1);
        Assign(apartment, P("Structure"), 2);
        Assign(apartment, P("MEP"), 3);
    }
}
