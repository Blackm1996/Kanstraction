# On-Site DDD refactor status

## Architecture now enforced

- `Kanstraction.Desktop` is the executable composition root.
- `Kanstraction.Presentation.Wpf` references only `Kanstraction.OnSite.Application`.
- `Kanstraction.OnSite.Application` references only Domain and application-level packages.
- `Kanstraction.OnSite.Infrastructure` owns EF Core, SQLite, ClosedXML, PDF rendering,
  migrations, backup/restore, and repository implementations.
- `Kanstraction.OnSite.Domain` has no EF, MediatR, WPF, localization, or I/O dependency.
- Architecture tests enforce these assembly boundaries, including the Presentation boundary.

## Persistence and application boundaries

- No `DbContext`, `DbSet`, `SaveChanges`, SQL, transaction, EF, Infrastructure, or
  ClosedXML reference remains in Presentation.
- Commands and queries execute through a scoped dispatcher; every operation gets a fresh
  async DI scope and scoped `OnSiteDbContext`.
- The former `AppDbContext` is now `OnSiteDbContext` and requires injected
  `DbContextOptions`; no parameterless context or `OnConfiguring` fallback remains.
- Startup migration and restore use `IDbContextFactory<OnSiteDbContext>`.
- Project/building-code uniqueness is enforced both by application behavior and by the
  existing migration-backed database constraint.
- Stage-preset, building-type, material, operations, payment, and reporting workflows now
  enter through Application contracts.
- Excel and PDF construction are Infrastructure adapters behind Application ports.
- EF reports no pending model changes, preserving the existing migration history and
  `%AppData%/Kanstraction/app.db`.

## Domain model

- Domain entities expose no public setters.
- Aggregate child collections are read-only to external callers and use EF field mapping.
- Building execution sequencing, one-active-sub-stage, lifecycle rollups, payment,
  stopping, labor validation, material quantity changes, and deletion eligibility are
  protected by domain behavior.
- Project, material/category, price history, stage preset, building type, and template
  association creation/update use named factories or methods.
- Status and progress calculations share one Domain policy; Application exposes a
  presentation-safe `WorkStatusDto`.

## Presentation

- Views bind Application DTOs or typed screen rows rather than tracked entities.
- `App.Services`, retained contexts, `ChangeTracker.Clear`, `dynamic` screen rows, and
  placeholder file export code have been removed.
- `ProjectsExplorerViewModel` owns typed project state, filtering, selection, and
  busy/reentrancy behavior.
- The remaining large Operations, Administration, and stage-preset files are now
  presentation-only. Their continued conversion from event-driven screen orchestration
  to bindable ViewModels is desirable, but it is no longer a data-access or business-rule
  boundary risk and can proceed incrementally without blocking a sibling module.

## Verification

- All test projects live physically and logically under `Tests`.
- `dotnet build Kanstraction.sln --no-restore`: zero warnings, zero errors.
- `dotnet test Kanstraction.sln --no-build --no-restore`: 26 passing tests.
- `dotnet ef migrations has-pending-model-changes`: no pending model changes.

## Next module rule

Procurement and Accounting should be introduced as sibling Domain/Application/
Infrastructure projects. They must communicate with On-Site through application contracts
or integration events, never through cross-module EF navigation properties.
