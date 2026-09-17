---
id: B025
slice: S26
title: Shop module persistence and skeleton
agent: backend-mentor
source: tasks/slices/026-shop-catalog.md
---

# Objective

Create the new `TenantForge.Modules.Shop` project skeleton — its own
`domain/`, `features/`, `infrastructure/` folders, a `ShopDbContext`, and
the two-method composition seam (`AddShopModule` before `Build`,
`UseShopModuleAsync` after `Build`) mirroring IAM's current
`AddIamModule`/`UseIamModuleAsync` shape — plus one EF Core migration for
the six catalog entities: `ShopCategory`, `ShopProduct`,
`ShopProductVariant`, `ShopSizeGuideColumn`, `ShopSizeGuideRow`,
`ShopSizeGuideCell`. No HTTP endpoint is added in this task.

This Spec gives you every file's exact path and exact code. Follow it
literally — do not invent a different folder name, class name or
namespace than what is written below.

# Context

Read `tasks/slices/026-shop-catalog.md` completely first.

This task builds a second business module beside the existing
`TenantForge.Modules.Iam`. Every piece of code below is copied,
field-for-field, from a real IAM file. The real files this Spec's code is
modeled on (open and compare if anything below is unclear):

- `src/modules/iam/TenantForge.Modules.Iam/domain/Tenant.cs` — domain
  entity shape.
- `src/modules/iam/TenantForge.Modules.Iam/infrastructure/TenantMap.cs`
  and `TsidValueConverter.cs` — EF Core map and value-converter shape.
- `src/modules/iam/TenantForge.Modules.Iam/infrastructure/IamDbContext.cs`
  — `DbContext` shape.
- `src/modules/iam/TenantForge.Modules.Iam/IamModule.cs` and
  `IAMConfig.cs` — the composition seam and `IModuleConfig`
  implementation.
- `src/api/TenantForge.Api/Program.cs` — where the seam is called from
  the host.
- `src/modules/iam/TenantForge.Modules.Iam/TenantForge.Modules.Iam.csproj`
  — package references to copy.

## A decision this task makes, stated once, used by every later Shop task

**`Shop:ShopDb` points at the exact same PostgreSQL database as
`IAM:IamDb`** (the same `Database=tenantforge` server/database already in
`appsettings.Development.json`), not a separate database. This is
deliberate: a later admin task (B026, B029) must check whether the
signed-in caller is a member of the tenant in the route, and IAM's own
tenant-membership fact lives in the `iam_tenant_memberships` table.
Because `TenantForge.Modules.Shop` must never take a `ProjectReference`
on `TenantForge.Modules.Iam` (modules do not reference each other — only
`TenantForge.BuildingBlocks` and their own future Contract project, per
B018/S20), Shop cannot query `IamDbContext.TenantMemberships` directly in
C#. Instead, `ShopDbContext` runs a **raw SQL** query against
`iam_tenant_memberships`/`iam_accounts`/`iam_tenants` (introduced fully in
B026, the first task that needs it) — which only works if both modules'
tables live in the same physical database. `ShopDbContext` never gets an
EF entity/`DbSet` for any `iam_*` table; it only ever reads those tables
with a raw SQL string. Because two `DbContext`s now share one physical
database, Shop's own EF Core migrations history table must have a
different name than IAM's default (`__EFMigrationsHistory`) or the two
modules' migration histories will collide — this task names it
`__ShopMigrationsHistory` explicitly (see step 8 below).

# Scope — every file, in order

## 1. `TenantForge.Modules.Shop.csproj`

Create `src/modules/shop/TenantForge.Modules.Shop/TenantForge.Modules.Shop.csproj`
with this exact content:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.4">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.3" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\building-blocks\TenantForge.BuildingBlocks\TenantForge.BuildingBlocks.csproj" />
  </ItemGroup>

</Project>
```

## 2. Add the project to the solution

From the repository root, after step 1 exists on disk:

```bash
dotnet sln TenantForge.sln add src/modules/shop/TenantForge.Modules.Shop/TenantForge.Modules.Shop.csproj
```

## 3. Reference Shop from the API host

Edit `src/api/TenantForge.Api/TenantForge.Api.csproj`. It currently ends
with:

```xml
  <ItemGroup>
    <ProjectReference Include="..\..\modules\iam\TenantForge.Modules.Iam\TenantForge.Modules.Iam.csproj" />
  </ItemGroup>

</Project>
```

Change that `<ItemGroup>` to:

```xml
  <ItemGroup>
    <ProjectReference Include="..\..\modules\iam\TenantForge.Modules.Iam\TenantForge.Modules.Iam.csproj" />
    <ProjectReference Include="..\..\modules\shop\TenantForge.Modules.Shop\TenantForge.Modules.Shop.csproj" />
  </ItemGroup>

</Project>
```

## 4. `src/modules/shop/TenantForge.Modules.Shop/infrastructure/ShopTsidValueConverter.cs`

Exact copy of IAM's converter, renamed and re-namespaced (Shop cannot use
IAM's `internal` class from a different assembly, so it needs its own):

```csharp
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TSID.Creator.NET;

namespace TenantForge.Modules.Shop.Infrastructure;

/// <summary>
/// The single EF Core bridge between the Shop identifier's domain type
/// (<see cref="Tsid"/>) and its provider type (signed 64-bit <c>long</c>,
/// stored as PostgreSQL <c>bigint</c>). Every Shop entity map applies this
/// one converter type to its <c>Tsid</c> properties — mirrors
/// TenantForge.Modules.Iam.Infrastructure.TsidValueConverter exactly, but
/// Shop needs its own copy because it is a separate assembly and the IAM
/// class is internal.
/// </summary>
internal sealed class ShopTsidValueConverter : ValueConverter<Tsid, long>
{
    public static readonly ShopTsidValueConverter Shared = new();

    private ShopTsidValueConverter()
        : base(tsid => tsid.ToLong(), value => Tsid.From(value))
    {
    }
}
```

## 5. Domain entities — `src/modules/shop/TenantForge.Modules.Shop/domain/`

Create each file exactly as written. Every entity follows the same shape
as `TenantForge.Modules.Iam.Domain.Tenant`: `internal sealed class`,
private setters, a private parameterless constructor, and a public
static `Create` factory that validates and stamps a fresh `Tsid`.

**`src/modules/shop/TenantForge.Modules.Shop/domain/ShopCategory.cs`:**

```csharp
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Domain;

internal sealed class ShopCategory
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; } = true;

    private ShopCategory()
    {
    }

    public static ShopCategory Create(Tsid tenantId, string name, string slug, int displayOrder)
    {
        if (TsidId.IsDefault(tenantId))
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Category name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("Category slug is required.", nameof(slug));
        }

        return new ShopCategory
        {
            Id = TsidId.NewId(),
            TenantId = tenantId,
            Name = name.Trim(),
            Slug = slug.Trim().ToLowerInvariant(),
            DisplayOrder = displayOrder,
            IsActive = true
        };
    }

    public void Update(string name, string slug, int displayOrder, bool isActive)
    {
        Name = name.Trim();
        Slug = slug.Trim().ToLowerInvariant();
        DisplayOrder = displayOrder;
        IsActive = isActive;
    }
}
```

**`src/modules/shop/TenantForge.Modules.Shop/domain/ShopProduct.cs`:**

```csharp
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Domain;

internal sealed class ShopProduct
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid TenantId { get; private set; }
    public Tsid CategoryId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public decimal BasePrice { get; private set; }
    public decimal? CompareAtPrice { get; private set; }
    public bool IsActive { get; private set; } = true;

    private ShopProduct()
    {
    }

    public static ShopProduct Create(
        Tsid tenantId,
        Tsid categoryId,
        string name,
        string slug,
        string description,
        decimal basePrice,
        decimal? compareAtPrice)
    {
        if (TsidId.IsDefault(tenantId))
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Product name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("Product slug is required.", nameof(slug));
        }

        return new ShopProduct
        {
            Id = TsidId.NewId(),
            TenantId = tenantId,
            CategoryId = categoryId,
            Name = name.Trim(),
            Slug = slug.Trim().ToLowerInvariant(),
            Description = description.Trim(),
            BasePrice = basePrice,
            CompareAtPrice = compareAtPrice,
            IsActive = true
        };
    }

    public void Update(
        Tsid categoryId,
        string name,
        string slug,
        string description,
        decimal basePrice,
        decimal? compareAtPrice,
        bool isActive)
    {
        CategoryId = categoryId;
        Name = name.Trim();
        Slug = slug.Trim().ToLowerInvariant();
        Description = description.Trim();
        BasePrice = basePrice;
        CompareAtPrice = compareAtPrice;
        IsActive = isActive;
    }
}
```

**`src/modules/shop/TenantForge.Modules.Shop/domain/ShopProductVariant.cs`:**

```csharp
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Domain;

internal sealed class ShopProductVariant
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid ProductId { get; private set; }
    public string Color { get; private set; } = string.Empty;
    public string Size { get; private set; } = string.Empty;
    public string Sku { get; private set; } = string.Empty;
    public int StockQuantity { get; private set; }
    public decimal? PriceOverride { get; private set; }

    private ShopProductVariant()
    {
    }

    public static ShopProductVariant Create(
        Tsid productId,
        string color,
        string size,
        string sku,
        int stockQuantity,
        decimal? priceOverride)
    {
        if (TsidId.IsDefault(productId))
        {
            throw new ArgumentException("Product id is required.", nameof(productId));
        }

        if (stockQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stockQuantity), "Stock quantity cannot be negative.");
        }

        return new ShopProductVariant
        {
            Id = TsidId.NewId(),
            ProductId = productId,
            Color = color.Trim(),
            Size = size.Trim(),
            Sku = sku.Trim(),
            StockQuantity = stockQuantity,
            PriceOverride = priceOverride
        };
    }

    /// <summary>
    /// Decrements stock by <paramref name="quantity"/>. Returns false (and
    /// changes nothing) when there is not enough stock, so the caller can
    /// answer with a clear "insufficient stock" result instead of allowing a
    /// negative row.
    /// </summary>
    public bool TryReserve(int quantity)
    {
        if (quantity <= 0 || StockQuantity < quantity)
        {
            return false;
        }

        StockQuantity -= quantity;
        return true;
    }
}
```

**`src/modules/shop/TenantForge.Modules.Shop/domain/ShopSizeGuideColumn.cs`:**

```csharp
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Domain;

internal sealed class ShopSizeGuideColumn
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid ProductId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int DisplayOrder { get; private set; }

    private ShopSizeGuideColumn()
    {
    }

    public static ShopSizeGuideColumn Create(Tsid productId, string name, int displayOrder)
    {
        if (TsidId.IsDefault(productId))
        {
            throw new ArgumentException("Product id is required.", nameof(productId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Column name is required.", nameof(name));
        }

        return new ShopSizeGuideColumn
        {
            Id = TsidId.NewId(),
            ProductId = productId,
            Name = name.Trim(),
            DisplayOrder = displayOrder
        };
    }
}
```

**`src/modules/shop/TenantForge.Modules.Shop/domain/ShopSizeGuideRow.cs`:**

```csharp
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Domain;

internal sealed class ShopSizeGuideRow
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid ProductId { get; private set; }
    public string SizeLabel { get; private set; } = string.Empty;
    public int DisplayOrder { get; private set; }

    private ShopSizeGuideRow()
    {
    }

    public static ShopSizeGuideRow Create(Tsid productId, string sizeLabel, int displayOrder)
    {
        if (TsidId.IsDefault(productId))
        {
            throw new ArgumentException("Product id is required.", nameof(productId));
        }

        if (string.IsNullOrWhiteSpace(sizeLabel))
        {
            throw new ArgumentException("Size label is required.", nameof(sizeLabel));
        }

        return new ShopSizeGuideRow
        {
            Id = TsidId.NewId(),
            ProductId = productId,
            SizeLabel = sizeLabel.Trim(),
            DisplayOrder = displayOrder
        };
    }
}
```

**`src/modules/shop/TenantForge.Modules.Shop/domain/ShopSizeGuideCell.cs`:**

```csharp
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Domain;

internal sealed class ShopSizeGuideCell
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid RowId { get; private set; }
    public Tsid ColumnId { get; private set; }
    public string Value { get; private set; } = string.Empty;

    private ShopSizeGuideCell()
    {
    }

    public static ShopSizeGuideCell Create(Tsid rowId, Tsid columnId, string value)
    {
        if (TsidId.IsDefault(rowId))
        {
            throw new ArgumentException("Row id is required.", nameof(rowId));
        }

        if (TsidId.IsDefault(columnId))
        {
            throw new ArgumentException("Column id is required.", nameof(columnId));
        }

        return new ShopSizeGuideCell
        {
            Id = TsidId.NewId(),
            RowId = rowId,
            ColumnId = columnId,
            Value = value?.Trim() ?? string.Empty
        };
    }
}
```

## 6. EF Core maps — `src/modules/shop/TenantForge.Modules.Shop/infrastructure/`

Each map follows `TenantMap.cs`'s exact shape: `ToTable`, `HasKey`,
`Property(...).HasConversion(ShopTsidValueConverter.Shared).ValueGeneratedNever()`
on the key, plain `Property(...)` calls for everything else, `HasIndex`
for uniqueness, `HasOne(...).WithMany().HasForeignKey(...)` for
relationships.

**`src/modules/shop/TenantForge.Modules.Shop/infrastructure/ShopCategoryMap.cs`:**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Infrastructure;

internal sealed class ShopCategoryMap : IEntityTypeConfiguration<ShopCategory>
{
    public void Configure(EntityTypeBuilder<ShopCategory> builder)
    {
        builder.ToTable("shop_categories");

        builder.HasKey(category => category.Id);

        builder.Property(category => category.Id)
            .HasColumnName("id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .ValueGeneratedNever();

        builder.Property(category => category.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(category => category.Name)
            .HasColumnName("name")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(category => category.Slug)
            .HasColumnName("slug")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(category => category.DisplayOrder)
            .HasColumnName("display_order")
            .IsRequired();

        builder.Property(category => category.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.HasIndex(category => new { category.TenantId, category.Slug })
            .IsUnique()
            .HasDatabaseName("ix_shop_categories_tenant_slug");
    }
}
```

**`src/modules/shop/TenantForge.Modules.Shop/infrastructure/ShopProductMap.cs`:**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Infrastructure;

internal sealed class ShopProductMap : IEntityTypeConfiguration<ShopProduct>
{
    public void Configure(EntityTypeBuilder<ShopProduct> builder)
    {
        builder.ToTable("shop_products");

        builder.HasKey(product => product.Id);

        builder.Property(product => product.Id)
            .HasColumnName("id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .ValueGeneratedNever();

        builder.Property(product => product.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(product => product.CategoryId)
            .HasColumnName("category_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(product => product.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(product => product.Slug)
            .HasColumnName("slug")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(product => product.Description)
            .HasColumnName("description")
            .HasMaxLength(4000)
            .IsRequired();

        builder.Property(product => product.BasePrice)
            .HasColumnName("base_price")
            .HasColumnType("numeric(12,2)")
            .IsRequired();

        builder.Property(product => product.CompareAtPrice)
            .HasColumnName("compare_at_price")
            .HasColumnType("numeric(12,2)");

        builder.Property(product => product.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.HasIndex(product => new { product.TenantId, product.Slug })
            .IsUnique()
            .HasDatabaseName("ix_shop_products_tenant_slug");

        builder.HasIndex(product => product.CategoryId)
            .HasDatabaseName("ix_shop_products_category_id");

        builder.HasOne<ShopCategory>()
            .WithMany()
            .HasForeignKey(product => product.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

**`src/modules/shop/TenantForge.Modules.Shop/infrastructure/ShopProductVariantMap.cs`:**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Infrastructure;

internal sealed class ShopProductVariantMap : IEntityTypeConfiguration<ShopProductVariant>
{
    public void Configure(EntityTypeBuilder<ShopProductVariant> builder)
    {
        builder.ToTable("shop_product_variants");

        builder.HasKey(variant => variant.Id);

        builder.Property(variant => variant.Id)
            .HasColumnName("id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .ValueGeneratedNever();

        builder.Property(variant => variant.ProductId)
            .HasColumnName("product_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(variant => variant.Color)
            .HasColumnName("color")
            .HasMaxLength(60)
            .IsRequired();

        builder.Property(variant => variant.Size)
            .HasColumnName("size")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(variant => variant.Sku)
            .HasColumnName("sku")
            .HasMaxLength(60)
            .IsRequired();

        builder.Property(variant => variant.StockQuantity)
            .HasColumnName("stock_quantity")
            .IsRequired();

        builder.Property(variant => variant.PriceOverride)
            .HasColumnName("price_override")
            .HasColumnType("numeric(12,2)");

        builder.HasIndex(variant => new { variant.ProductId, variant.Color, variant.Size })
            .IsUnique()
            .HasDatabaseName("ix_shop_product_variants_product_color_size");

        builder.HasOne<ShopProduct>()
            .WithMany()
            .HasForeignKey(variant => variant.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

**`src/modules/shop/TenantForge.Modules.Shop/infrastructure/ShopSizeGuideColumnMap.cs`:**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Infrastructure;

internal sealed class ShopSizeGuideColumnMap : IEntityTypeConfiguration<ShopSizeGuideColumn>
{
    public void Configure(EntityTypeBuilder<ShopSizeGuideColumn> builder)
    {
        builder.ToTable("shop_size_guide_columns");

        builder.HasKey(column => column.Id);

        builder.Property(column => column.Id)
            .HasColumnName("id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .ValueGeneratedNever();

        builder.Property(column => column.ProductId)
            .HasColumnName("product_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(column => column.Name)
            .HasColumnName("name")
            .HasMaxLength(60)
            .IsRequired();

        builder.Property(column => column.DisplayOrder)
            .HasColumnName("display_order")
            .IsRequired();

        builder.HasIndex(column => column.ProductId)
            .HasDatabaseName("ix_shop_size_guide_columns_product_id");

        builder.HasOne<ShopProduct>()
            .WithMany()
            .HasForeignKey(column => column.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

**`src/modules/shop/TenantForge.Modules.Shop/infrastructure/ShopSizeGuideRowMap.cs`:**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Infrastructure;

internal sealed class ShopSizeGuideRowMap : IEntityTypeConfiguration<ShopSizeGuideRow>
{
    public void Configure(EntityTypeBuilder<ShopSizeGuideRow> builder)
    {
        builder.ToTable("shop_size_guide_rows");

        builder.HasKey(row => row.Id);

        builder.Property(row => row.Id)
            .HasColumnName("id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .ValueGeneratedNever();

        builder.Property(row => row.ProductId)
            .HasColumnName("product_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(row => row.SizeLabel)
            .HasColumnName("size_label")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(row => row.DisplayOrder)
            .HasColumnName("display_order")
            .IsRequired();

        builder.HasIndex(row => row.ProductId)
            .HasDatabaseName("ix_shop_size_guide_rows_product_id");

        builder.HasOne<ShopProduct>()
            .WithMany()
            .HasForeignKey(row => row.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

**`src/modules/shop/TenantForge.Modules.Shop/infrastructure/ShopSizeGuideCellMap.cs`:**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Infrastructure;

internal sealed class ShopSizeGuideCellMap : IEntityTypeConfiguration<ShopSizeGuideCell>
{
    public void Configure(EntityTypeBuilder<ShopSizeGuideCell> builder)
    {
        builder.ToTable("shop_size_guide_cells");

        builder.HasKey(cell => cell.Id);

        builder.Property(cell => cell.Id)
            .HasColumnName("id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .ValueGeneratedNever();

        builder.Property(cell => cell.RowId)
            .HasColumnName("row_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(cell => cell.ColumnId)
            .HasColumnName("column_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(cell => cell.Value)
            .HasColumnName("value")
            .HasMaxLength(60)
            .IsRequired();

        builder.HasIndex(cell => new { cell.RowId, cell.ColumnId })
            .IsUnique()
            .HasDatabaseName("ix_shop_size_guide_cells_row_column");

        builder.HasOne<ShopSizeGuideRow>()
            .WithMany()
            .HasForeignKey(cell => cell.RowId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ShopSizeGuideColumn>()
            .WithMany()
            .HasForeignKey(cell => cell.ColumnId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

## 7. `src/modules/shop/TenantForge.Modules.Shop/infrastructure/ShopDbContext.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Infrastructure;

internal sealed class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options)
{
    internal DbSet<ShopCategory> Categories => Set<ShopCategory>();
    internal DbSet<ShopProduct> Products => Set<ShopProduct>();
    internal DbSet<ShopProductVariant> ProductVariants => Set<ShopProductVariant>();
    internal DbSet<ShopSizeGuideColumn> SizeGuideColumns => Set<ShopSizeGuideColumn>();
    internal DbSet<ShopSizeGuideRow> SizeGuideRows => Set<ShopSizeGuideRow>();
    internal DbSet<ShopSizeGuideCell> SizeGuideCells => Set<ShopSizeGuideCell>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ShopCategoryMap());
        modelBuilder.ApplyConfiguration(new ShopProductMap());
        modelBuilder.ApplyConfiguration(new ShopProductVariantMap());
        modelBuilder.ApplyConfiguration(new ShopSizeGuideColumnMap());
        modelBuilder.ApplyConfiguration(new ShopSizeGuideRowMap());
        modelBuilder.ApplyConfiguration(new ShopSizeGuideCellMap());
    }
}
```

## 8. `src/modules/shop/TenantForge.Modules.Shop/ShopConfig.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TenantForge.BuildingBlocks.Modules;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop;

public sealed class ShopConfig : IModuleConfig
{
    public string SectionName => "Shop";

    private string ShopConnectionStringPath => $"{SectionName}:ShopDb";

    public void RegisterServices(IServiceCollection services, IHostEnvironment environment)
    {
        services.AddDbContext<ShopDbContext>((serviceProvider, options) =>
        {
            var connectionString = serviceProvider.GetRequiredService<IConfiguration>()[ShopConnectionStringPath];
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                // Shop:ShopDb points at the same physical database as
                // IAM:IamDb (see this Spec's Context section). Each module's
                // migration history must be tracked in its own table or the
                // two modules' migration histories collide.
                npgsqlOptions.MigrationsHistoryTable("__ShopMigrationsHistory");
            });
        });
    }

    public void ValidateConfiguration(IHostEnvironment environment, IConfiguration configuration)
    {
        var connectionString = configuration[ShopConnectionStringPath];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"The '{ShopConnectionStringPath}' configuration value is required for Shop persistence.");
        }
    }
}
```

## 9. `src/modules/shop/TenantForge.Modules.Shop/ShopModule.cs`

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TenantForge.BuildingBlocks.Modules;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop;

public static class ShopModule
{
    private static readonly IModuleConfig Config = new ShopConfig();

    /// <summary>
    /// Registration phase: mirrors IamModule.AddIamModule exactly — adds
    /// every Shop-owned service to the container, does no I/O and no
    /// pass/fail decision. Runs before builder.Build().
    /// </summary>
    public static IServiceCollection AddShopModule(this IServiceCollection services, IHostEnvironment environment)
    {
        Config.RegisterServices(services, environment);
        return services;
    }

    /// <summary>
    /// Activation phase: mirrors IamModule.UseIamModuleAsync. This task adds
    /// only configuration validation (fail closed) and pending migrations —
    /// there is no endpoint to map and no seed step yet.
    /// </summary>
    public static async Task UseShopModuleAsync(this WebApplication app)
    {
        ValidateShopModuleConfiguration(app.Environment, app.Configuration);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
        await db.Database.MigrateAsync();
    }

    private static void ValidateShopModuleConfiguration(IHostEnvironment environment, IConfiguration configuration)
    {
        Config.ValidateConfiguration(environment, configuration);
    }
}
```

## 10. `src/api/TenantForge.Api/Program.cs`

The file currently reads:

```csharp
using TenantForge.Api;
using TenantForge.Modules.Iam;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>())
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

builder.Services.AddIamModule(builder.Environment);

var app = builder.Build();

app.UseCors();

// IAM activation owns, in deterministic order: configuration validation
// (fail closed), authentication middleware, authorization middleware,
// pending migrations, idempotent platform-administrator seeding, and mapping
// every IAM endpoint. The host does not call any of those steps separately.
await app.UseIamModuleAsync();

app.MapHealth();

app.Run();

public partial class Program;
```

Change it to exactly this (one new `using`, one new registration call,
one new activation call — nothing else moves):

```csharp
using TenantForge.Api;
using TenantForge.Modules.Iam;
using TenantForge.Modules.Shop;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>())
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

builder.Services.AddIamModule(builder.Environment);
builder.Services.AddShopModule(builder.Environment);

var app = builder.Build();

app.UseCors();

// IAM activation owns, in deterministic order: configuration validation
// (fail closed), authentication middleware, authorization middleware,
// pending migrations, idempotent platform-administrator seeding, and mapping
// every IAM endpoint. The host does not call any of those steps separately.
await app.UseIamModuleAsync();

// Shop activation owns: configuration validation (fail closed) and pending
// migrations. It maps no endpoint yet (B025 has none).
await app.UseShopModuleAsync();

app.MapHealth();

app.Run();

public partial class Program;
```

## 11. `src/api/TenantForge.Api/appsettings.Development.json`

The file currently reads:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedOrigins": [
    "http://localhost:5173"
  ],
  "IAM": {
    "IamDb": "Host=localhost;Port=5432;Database=tenantforge;Username=tenantforge;Password=tenantforge",
    "Auth": {
      "SigningKey": "dev-only-tenantforge-signing-key-do-not-use-32b"
    },
    "SeedAdmin": {
      "Email": "admin@tenantforge.local",
      "Password": "local-development-password",
      "DisplayName": "Platform Administrator"
    }
  }
}
```

Add a sibling `"Shop"` section with the **exact same connection string
value** as `"IAM"."IamDb"` (same database, per this Spec's Context):

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedOrigins": [
    "http://localhost:5173"
  ],
  "IAM": {
    "IamDb": "Host=localhost;Port=5432;Database=tenantforge;Username=tenantforge;Password=tenantforge",
    "Auth": {
      "SigningKey": "dev-only-tenantforge-signing-key-do-not-use-32b"
    },
    "SeedAdmin": {
      "Email": "admin@tenantforge.local",
      "Password": "local-development-password",
      "DisplayName": "Platform Administrator"
    }
  },
  "Shop": {
    "ShopDb": "Host=localhost;Port=5432;Database=tenantforge;Username=tenantforge;Password=tenantforge"
  }
}
```

Apply the same addition to whatever test-host configuration source the
integration test project uses to supply `IAM:IamDb` (grep
`tests/integration/TenantForge.Api.IntegrationTests/` for `IamDb` to find
every place that needs the matching `Shop:ShopDb` entry — likely a test
`appsettings.json` or an in-memory configuration builder used by the test
host fixture).

## 12. Migration

Run, from the repository root:

```bash
dotnet ef migrations add InitialShopCatalog --project src/modules/shop/TenantForge.Modules.Shop --startup-project src/api/TenantForge.Api --output-dir infrastructure/Migrations
```

This creates
`src/modules/shop/TenantForge.Modules.Shop/infrastructure/Migrations/<timestamp>_InitialShopCatalog.cs`
and the matching `.Designer.cs`/`ShopDbContextModelSnapshot.cs`. Do not
hand-edit the generated migration file; if the generated column
types/names do not match this Spec's maps exactly, fix the map file and
regenerate instead.

# If you get stuck

**The migration command fails with "no DbContext was found" or similar.**
Confirm `TenantForge.Modules.Shop.csproj` has the
`Microsoft.EntityFrameworkCore.Design` package reference (step 1) and
that `src/api/TenantForge.Api/TenantForge.Api.csproj` has the new
`ProjectReference` (step 3) — `dotnet ef` resolves the design-time
context through the startup project's build output.

**Confirm the migration applied**, after running the API once
(`dotnet run --project src/api/TenantForge.Api`):

```bash
psql "host=localhost port=5432 dbname=tenantforge user=tenantforge password=tenantforge" -c "\dt shop_*"
```

Expected output: six rows, one per table listed in step 6 above, plus a
`__shopmigrationshistory` row (lowercased by Postgres).

# Acceptance

- The solution builds with `TenantForge.Modules.Shop` included and
  referenced from `TenantForge.Api`.
- `AddShopModule`/`UseShopModuleAsync` exist with exactly the same
  two-call shape IAM uses; `Program.cs` calls both, in the same
  registration-before-`Build`/activation-after-`Build` order as IAM.
- Starting the API with `Shop:ShopDb` unset fails startup with a clear
  message (fail closed) — verified with an integration test that boots a
  test host without the setting and asserts a startup failure.
- Starting the API with `Shop:ShopDb` set applies the migration and
  creates exactly the six tables listed above, plus
  `__ShopMigrationsHistory`.
- Restarting the API against the same already-migrated database is a
  no-op (idempotent).
- No HTTP endpoint is added or reachable from this module yet.
- `docs/modules/IAM.md` is unaffected by this task; state
  `IAM.md impact: none — this task only creates the new, unrelated
  TenantForge.Modules.Shop project` in self-review and the PR body.
  `BuildingBlocks docs impact: none — TsidId and IModuleConfig are
  consumed as-is by a second, expected consumer; neither type nor its
  admission rule changed`.

# Verification

Automated:

```bash
dotnet build TenantForge.sln --nologo
dotnet test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

The existing IAM-focused suite must continue to pass unmodified, proving
zero regression from adding the second module.

Manual: see "If you get stuck" above for the `psql` check; also boot the
API with `Shop:ShopDb` unset and confirm it fails to start with the
expected clear error.

# Lifecycle

Add row `B025` to the Backend queue in `tasks/TASKS.md` with status
`planned`, no dependency (the first Shop task), and Spec link
`tasks/backend/B025-shop-module-persistence-and-skeleton.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/026-shop-catalog.md` is the permanent record and is
never deleted.
