using System;
using System.Linq;
using System.Collections.Generic;
using Kanstraction.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kanstraction.Data
{
    /// <summary>
    /// Seeds presets ONLY:
    /// - Materials (+ a bit of price history)
    /// - StagePresets WITH SubStagePresets and MaterialUsagePresets
    /// - BuildingTypes WITH StagePreset assignments
    /// Safe to call multiple times; each Ensure* method is idempotent.
    /// </summary>
    public static class DbSeeder
    {
        private static readonly Dictionary<string, (int order, string name, decimal labor, (string mat, decimal qty)[] uses)[]> StagePresetSeeds = new()
        {
            ["Fondation"] = new[]
            {
                (1, "Excavation", 800m,  new[] { ("Sable", 5m), ("Gravier", 3m) }),
                (2, "Armature",   1200m, new[] { ("Armature", 500m) }),
                (3, "Coulage",    1500m, new[] { ("Ciment", 60m), ("Sable", 10m), ("Gravier", 10m) })
            },
            ["Structure"] = new[]
            {
                (1, "Colonnes", 1400m, new[] { ("Armature", 600m), ("Ciment", 40m) }),
                (2, "Poutres",  1200m, new[] { ("Armature", 400m), ("Ciment", 30m) }),
                (3, "Dalle",    1300m, new[] { ("Armature", 300m), ("Ciment", 35m) })
            },
            ["Murs"] = new[]
            {
                (1, "Maçonnerie de blocs", 900m, new[] { ("Blocs", 1000m), ("Ciment", 20m), ("Sable", 8m) }),
                (2, "Plâtrage",            700m, new[] { ("Plâtre", 30m) })
            },
            ["MEP"] = new[]
            {
                (1, "Préinstallation plomberie",  800m, new[] { ("Tuyaux", 120m) }),
                (2, "Préinstallation électrique", 900m, new[] { ("Câblage", 300m) })
            },
            ["Finitions"] = new[]
            {
                (1, "Apprêt",          500m, new[] { ("Peinture", 5m) }),
                (2, "Peinture finale", 700m, new[] { ("Peinture", 10m) })
            }
        };

        public static void Seed(AppDbContext db)
        {
            // No early return: let each Ensure* handle its own idempotency
            var materialsByName = EnsureMaterials(db);
            EnsureStagePresets(db, materialsByName);
            EnsureBuildingTypes(db);
        }

        // ---------------- MATERIALS ----------------
        private static Dictionary<string, Material> EnsureMaterials(AppDbContext db)
        {
            // Target materials (French set — keep consistent names)
            var wanted = new[]
            {
                new Material { Name = "Ciment",   Unit = "sac", PricePerUnit =  6.50m, EffectiveSince = DateTime.Today.AddMonths(-4), IsActive = true },
                new Material { Name = "Sable",    Unit = "m³",  PricePerUnit = 12.00m, EffectiveSince = DateTime.Today.AddMonths(-3), IsActive = true },
                new Material { Name = "Gravier",  Unit = "m³",  PricePerUnit = 15.00m, EffectiveSince = DateTime.Today.AddMonths(-3), IsActive = true },
                new Material { Name = "Armature", Unit = "kg",  PricePerUnit =  1.20m, EffectiveSince = DateTime.Today.AddMonths(-5), IsActive = true },
                new Material { Name = "Blocs",    Unit = "pcs", PricePerUnit =  0.90m, EffectiveSince = DateTime.Today.AddMonths(-2), IsActive = true },
                new Material { Name = "Peinture", Unit = "gal", PricePerUnit = 18.00m, EffectiveSince = DateTime.Today.AddMonths(-1), IsActive = true },
                new Material { Name = "Câblage",  Unit = "m",   PricePerUnit =  0.35m, EffectiveSince = DateTime.Today.AddMonths(-6), IsActive = true },
                new Material { Name = "Tuyaux",   Unit = "m",   PricePerUnit =  0.80m, EffectiveSince = DateTime.Today.AddMonths(-6), IsActive = true },
                new Material { Name = "Plâtre",   Unit = "sac", PricePerUnit =  7.20m, EffectiveSince = DateTime.Today.AddMonths(-2), IsActive = true },
            };

            // Upsert materials by (case-insensitive) name
            var existing = db.Materials.AsNoTracking().ToList();
            var existingByName = existing
                .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var w in wanted)
            {
                if (!existingByName.TryGetValue(w.Name, out var found))
                {
                    db.Materials.Add(w);
                }
                else
                {
                    // Keep price/unit/unit text in sync if you want — or skip to preserve edits
                    // found.Unit = w.Unit; found.PricePerUnit = w.PricePerUnit; found.EffectiveSince = w.EffectiveSince; found.IsActive = w.IsActive;
                    // db.Materials.Update(found);
                }
            }
            db.SaveChanges();

            // Rebuild the dictionary after potential inserts
            var byName = db.Materials.ToList()
                .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // --- Seed a tiny history for Paint/Peinture (idempotent) ---
            // Accept either "Peinture" (seeded) or "Paint" (if user renamed)
            var paintName = byName.ContainsKey("Peinture") ? "Peinture" :
                            byName.ContainsKey("Paint") ? "Paint" : null;

            if (paintName != null)
            {
                var paint = byName[paintName];
                var hasAny = db.MaterialPriceHistory.Any(h => h.MaterialId == paint.Id);
                if (!hasAny)
                {
                    db.MaterialPriceHistory.AddRange(
                        new MaterialPriceHistory
                        {
                            MaterialId = paint.Id,
                            PricePerUnit = 16.00m,
                            StartDate = DateTime.Today.AddMonths(-7),
                            EndDate = DateTime.Today.AddMonths(-1)
                        },
                        new MaterialPriceHistory
                        {
                            MaterialId = paint.Id,
                            PricePerUnit = 18.00m,
                            StartDate = DateTime.Today.AddMonths(-1),
                            EndDate = null
                        }
                    );
                    db.SaveChanges();
                }
            }
            else
            {
                // Optional: log/trace that "Peinture/Paint" wasn't found; not fatal.
            }

            return byName;
        }

        // ---------------- STAGE PRESETS (+ SUBS + MATERIAL USAGES) ----------------
        private static void EnsureStagePresets(AppDbContext db, Dictionary<string, Material> materialsByName)
        {
            foreach (var (presetName, subs) in StagePresetSeeds)
            {
                var preset = db.StagePresets.FirstOrDefault(p => p.Name == presetName);
                if (preset == null)
                {
                    preset = new StagePreset { Name = presetName, IsActive = true };
                    db.StagePresets.Add(preset);
                    db.SaveChanges();
                }
                else if (!preset.IsActive)
                {
                    preset.IsActive = true;
                    db.SaveChanges();
                }

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
                            OrderIndex = s.order
                        };
                        db.SubStagePresets.Add(sub);
                        db.SaveChanges();
                    }
                    else
                    {
                        if (sub.OrderIndex != s.order || !string.Equals(sub.Name, s.name, StringComparison.Ordinal))
                        {
                            sub.OrderIndex = s.order;
                            sub.Name = s.name;
                            db.SubStagePresets.Update(sub);
                            db.SaveChanges();
                        }
                    }

                    foreach (var (matName, qty) in s.uses)
                    {
                        if (!materialsByName.TryGetValue(matName, out var mat)) continue;

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
                        else if (exists.Qty != qty)
                        {
                            exists.Qty = qty;
                        }
                    }
                    db.SaveChanges();
                }
            }
        }

        // ---------------- BUILDING TYPES ----------------
        private static void EnsureBuildingTypes(AppDbContext db)
        {
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
            var apartment = EnsureType("Appartement");

            // Use a stable snapshot of presets
            var presets = db.StagePresets.AsNoTracking().ToList();
            StagePreset P(string name)
            {
                var p = presets.FirstOrDefault(x => x.Name == name);
                if (p == null) throw new InvalidOperationException($"StagePreset '{name}' not found. Seed StagePresets first.");
                return p;
            }

            void EnsureLabor(BuildingType t, StagePreset preset)
            {
                if (!StagePresetSeeds.TryGetValue(preset.Name, out var subs)) return;

                foreach (var s in subs)
                {
                    var subPreset = db.SubStagePresets.FirstOrDefault(x => x.StagePresetId == preset.Id && x.Name == s.name);
                    if (subPreset == null) continue;

                    var row = db.BuildingTypeSubStageLabors
                        .FirstOrDefault(x => x.BuildingTypeId == t.Id && x.SubStagePresetId == subPreset.Id);

                    if (row == null)
                    {
                        db.BuildingTypeSubStageLabors.Add(new BuildingTypeSubStageLabor
                        {
                            BuildingTypeId = t.Id,
                            SubStagePresetId = subPreset.Id,
                            LaborCost = s.labor
                        });
                    }
                    else if (row.LaborCost != s.labor)
                    {
                        row.LaborCost = s.labor;
                    }
                }

                db.SaveChanges();
            }

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
                else if (exists.OrderIndex != order)
                {
                    exists.OrderIndex = order;
                    db.SaveChanges();
                }

                EnsureLabor(t, p);
            }

            // Villa: Fondation → Structure → Murs → MEP → Finitions
            var fondation = P("Fondation");
            var structure = P("Structure");
            var murs = P("Murs");
            var mep = P("MEP");
            var finitions = P("Finitions");

            Assign(villa, fondation, 1);
            Assign(villa, structure, 2);
            Assign(villa, murs, 3);
            Assign(villa, mep, 4);
            Assign(villa, finitions, 5);

            // Duplex: Fondation → Structure → Murs → Finitions
            Assign(duplex, fondation, 1);
            Assign(duplex, structure, 2);
            Assign(duplex, murs, 3);
            Assign(duplex, finitions, 4);

            // Appartement: Fondation → Structure → MEP
            Assign(apartment, fondation, 1);
            Assign(apartment, structure, 2);
            Assign(apartment, mep, 3);
        }
    }
}
