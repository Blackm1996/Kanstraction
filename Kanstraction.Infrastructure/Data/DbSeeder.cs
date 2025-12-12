using System;
using System.Linq;
using System.Collections.Generic;
using Kanstraction.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kanstraction.Infrastructure.Data
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
        private const string DefaultCategoryName = "Defaut";

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
            var defaultCategory = EnsureDefaultMaterialCategory(db);

            // Target materials (French set — keep consistent names)
            var wanted = new[]
            {
                new Material { Name = "Ciment",   Unit = "sac", PricePerUnit =  6.50m, EffectiveSince = DateTime.Today.AddMonths(-4), IsActive = true, MaterialCategoryId = defaultCategory.Id },
                new Material { Name = "Sable",    Unit = "m³",  PricePerUnit = 12.00m, EffectiveSince = DateTime.Today.AddMonths(-3), IsActive = true, MaterialCategoryId = defaultCategory.Id },
                new Material { Name = "Gravier",  Unit = "m³",  PricePerUnit = 15.00m, EffectiveSince = DateTime.Today.AddMonths(-3), IsActive = true, MaterialCategoryId = defaultCategory.Id },
                new Material { Name = "Armature", Unit = "kg",  PricePerUnit =  1.20m, EffectiveSince = DateTime.Today.AddMonths(-5), IsActive = true, MaterialCategoryId = defaultCategory.Id },
                new Material { Name = "Blocs",    Unit = "pcs", PricePerUnit =  0.90m, EffectiveSince = DateTime.Today.AddMonths(-2), IsActive = true, MaterialCategoryId = defaultCategory.Id },
                new Material { Name = "Peinture", Unit = "gal", PricePerUnit = 18.00m, EffectiveSince = DateTime.Today.AddMonths(-1), IsActive = true, MaterialCategoryId = defaultCategory.Id },
                new Material { Name = "Câblage",  Unit = "m",   PricePerUnit =  0.35m, EffectiveSince = DateTime.Today.AddMonths(-6), IsActive = true, MaterialCategoryId = defaultCategory.Id },
                new Material { Name = "Tuyaux",   Unit = "m",   PricePerUnit =  0.80m, EffectiveSince = DateTime.Today.AddMonths(-6), IsActive = true, MaterialCategoryId = defaultCategory.Id },
                new Material { Name = "Plâtre",   Unit = "sac", PricePerUnit =  7.20m, EffectiveSince = DateTime.Today.AddMonths(-2), IsActive = true, MaterialCategoryId = defaultCategory.Id },
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

        private static MaterialCategory EnsureDefaultMaterialCategory(AppDbContext db)
        {
            var existing = db.MaterialCategories.FirstOrDefault(c => c.Name == DefaultCategoryName);
            if (existing != null)
            {
                return existing;
            }

            var category = new MaterialCategory
            {
                Name = DefaultCategoryName
            };

            db.MaterialCategories.Add(category);
            db.SaveChanges();

            return category;
        }

        // ---------------- STAGE PRESETS (+ SUBS + MATERIAL USAGES) ----------------
        private static void EnsureStagePresets(AppDbContext db, Dictionary<string, Material> materialsByName)
        {
            // Local helper to add/update a StagePreset with its SubStagePresets and material usages
            void UpsertStagePreset(
                string presetName,
                (int order, string name, string[] uses)[] subs)
            {
                var preset = db.StagePresets.FirstOrDefault(p => p.Name == presetName);
                if (preset == null)
                {
                    preset = new StagePreset { Name = presetName, IsActive = true };
                    db.StagePresets.Add(preset);
                    db.SaveChanges(); // we need preset.Id for children
                }

                // Ensure sub-stages in order
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
                        db.SaveChanges(); // need sub.Id for material usages
                    }
                    else
                    {
                        // keep metadata updated (order)
                        if (sub.OrderIndex != s.order)
                        {
                            sub.OrderIndex = s.order;
                            db.SubStagePresets.Update(sub);
                            db.SaveChanges();
                        }
                    }

                    // Ensure material usages for this sub-stage preset
                    foreach (var matName in s.uses)
                    {
                        if (!materialsByName.TryGetValue(matName, out var mat)) continue;

                        var exists = db.MaterialUsagesPreset.FirstOrDefault(mu =>
                            mu.SubStagePresetId == sub.Id && mu.MaterialId == mat.Id);

                        if (exists == null)
                        {
                            db.MaterialUsagesPreset.Add(new MaterialUsagePreset
                            {
                                SubStagePresetId = sub.Id,
                                MaterialId = mat.Id
                            });
                        }
                    }
                    db.SaveChanges();
                }
            }

            // Presets (same as your current intent, names kept consistent)
            UpsertStagePreset("Fondation", new[]
            {
                (1, "Excavation",  new[] { "Sable", "Gravier" }),
                (2, "Armature",    new[] { "Armature" }),
                (3, "Coulage",     new[] { "Ciment", "Sable", "Gravier" })
            });

            UpsertStagePreset("Structure", new[]
            {
                (1, "Colonnes", new[] { "Armature", "Ciment" }),
                (2, "Poutres",  new[] { "Armature", "Ciment" }),
                (3, "Dalle",    new[] { "Armature", "Ciment" })
            });

            UpsertStagePreset("Murs", new[]
            {
                (1, "Maçonnerie de blocs", new[] { "Blocs", "Ciment", "Sable" }),
                (2, "Plâtrage",            new[] { "Plâtre" })
            });

            UpsertStagePreset("MEP", new[]
            {
                (1, "Préinstallation plomberie",  new[] { "Tuyaux" }),
                (2, "Préinstallation électrique", new[] { "Câblage" })
            });

            UpsertStagePreset("Finitions", new[]
            {
                (1, "Apprêt",          new[] { "Peinture" }),
                (2, "Peinture finale", new[] { "Peinture" })
            });
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
            }

            // Villa: Fondation → Structure → Murs → MEP → Finitions
            Assign(villa, P("Fondation"), 1);
            Assign(villa, P("Structure"), 2);
            Assign(villa, P("Murs"), 3);
            Assign(villa, P("MEP"), 4);
            Assign(villa, P("Finitions"), 5);

            // Duplex: Fondation → Structure → Murs → Finitions
            Assign(duplex, P("Fondation"), 1);
            Assign(duplex, P("Structure"), 2);
            Assign(duplex, P("Murs"), 3);
            Assign(duplex, P("Finitions"), 4);

            // Appartement: Fondation → Structure → MEP
            Assign(apartment, P("Fondation"), 1);
            Assign(apartment, P("Structure"), 2);
            Assign(apartment, P("MEP"), 3);
        }
    }
}
