using System;
using Kairo.Modules.Catalogue.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Kairo.Modules.Catalogue.Data.Migrations
{
    [DbContext(typeof(CatalogueDbContext))]
    [Migration("20261015070054_Catalogue")]
    partial class Catalogue
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasDefaultSchema("catalogue")
                .HasAnnotation("ProductVersion", "10.0.12")
                .HasAnnotation("Relational:MaxIdentifierLength", 128);

            SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder);

            modelBuilder.Entity("Kairo.Modules.Catalogue.Imports.CatalogueImport", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<int>("Added")
                        .HasColumnType("int");

                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("ContentHash")
                        .HasMaxLength(64)
                        .HasColumnType("nvarchar(64)");

                    b.Property<string>("ErrorCode")
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<string>("ErrorMessage")
                        .HasMaxLength(500)
                        .HasColumnType("nvarchar(500)");

                    b.Property<string>("FeedUrl")
                        .HasMaxLength(2000)
                        .HasColumnType("nvarchar(2000)");

                    b.Property<string>("FileName")
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<DateTimeOffset>("FinishedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<int>("Invalid")
                        .HasColumnType("int");

                    b.Property<string>("Kind")
                        .IsRequired()
                        .HasMaxLength(10)
                        .HasColumnType("nvarchar(10)");

                    b.Property<long>("Number")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint");

                    SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<long>("Number"));

                    b.Property<string>("Problems")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<int>("Removed")
                        .HasColumnType("int");

                    b.Property<int>("Rows")
                        .HasColumnType("int");

                    b.Property<DateTimeOffset>("StartedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid?>("StartedBy")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<string>("StorageKey")
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<int>("Unchanged")
                        .HasColumnType("int");

                    b.Property<int>("Updated")
                        .HasColumnType("int");

                    b.Property<int>("Valid")
                        .HasColumnType("int");

                    b.HasKey("Id");

                    SqlServerKeyBuilderExtensions.IsClustered(b.HasKey("Id"), false);

                    b.HasIndex("BrandId", "StartedAt");

                    SqlServerIndexBuilderExtensions.IsClustered(b.HasIndex("BrandId", "StartedAt"));

                    b.ToTable("Imports", "catalogue");
                });

            modelBuilder.Entity("Kairo.Modules.Catalogue.Imports.CatalogueSource", b =>
                {
                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<int>("FailuresInRow")
                        .HasColumnType("int");

                    b.Property<string>("FeedUrl")
                        .HasMaxLength(2000)
                        .HasColumnType("nvarchar(2000)");

                    b.Property<string>("Kind")
                        .IsRequired()
                        .HasMaxLength(10)
                        .HasColumnType("nvarchar(10)");

                    b.Property<DateTimeOffset?>("LastCheckedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<DateTimeOffset?>("LastSucceededAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<DateTimeOffset>("SetAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid>("SetBy")
                        .HasColumnType("uniqueidentifier");

                    b.HasKey("BrandId");

                    b.ToTable("Sources", "catalogue");
                });

            modelBuilder.Entity("Kairo.Modules.Catalogue.Products.Product", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.PrimitiveCollection<string>("AdditionalImageLinks")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Availability")
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<string>("Brand")
                        .HasMaxLength(70)
                        .HasColumnType("nvarchar(70)");

                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Category")
                        .HasMaxLength(750)
                        .HasColumnType("nvarchar(750)");

                    b.Property<string>("Currency")
                        .HasMaxLength(3)
                        .HasColumnType("nvarchar(3)");

                    b.Property<string>("Description")
                        .HasColumnType("nvarchar(max)");

                    b.Property<DateTimeOffset>("FirstSeenAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("GoogleCategory")
                        .HasMaxLength(750)
                        .HasColumnType("nvarchar(750)");

                    b.Property<string>("Gtin")
                        .HasMaxLength(14)
                        .HasColumnType("nvarchar(14)");

                    b.Property<string>("ImageLink")
                        .HasMaxLength(2000)
                        .HasColumnType("nvarchar(2000)");

                    b.Property<string>("ItemGroupId")
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<Guid>("LastImportId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Link")
                        .HasMaxLength(2000)
                        .HasColumnType("nvarchar(2000)");

                    b.Property<int>("Missing")
                        .HasColumnType("int");

                    b.Property<string>("OfferId")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<decimal?>("Price")
                        .HasPrecision(19, 4)
                        .HasColumnType("decimal(19,4)");

                    b.Property<DateTimeOffset?>("RemovedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<decimal?>("SalePrice")
                        .HasPrecision(19, 4)
                        .HasColumnType("decimal(19,4)");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<string>("Title")
                        .HasMaxLength(150)
                        .HasColumnType("nvarchar(150)");

                    b.Property<DateTimeOffset>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    SqlServerKeyBuilderExtensions.IsClustered(b.HasKey("Id"), false);

                    b.HasIndex("BrandId", "OfferId")
                        .IsUnique();

                    SqlServerIndexBuilderExtensions.IsClustered(b.HasIndex("BrandId", "OfferId"));

                    b.HasIndex("BrandId", "Status", "Missing");

                    b.ToTable("Products", "catalogue");
                });

            modelBuilder.Entity("Kairo.Modules.Catalogue.Products.StockEvent", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("At")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("From")
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<Guid>("ImportId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("ProductId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("To")
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.HasKey("Id");

                    SqlServerKeyBuilderExtensions.IsClustered(b.HasKey("Id"), false);

                    b.HasIndex("BrandId", "ProductId", "At");

                    SqlServerIndexBuilderExtensions.IsClustered(b.HasIndex("BrandId", "ProductId", "At"));

                    b.ToTable("StockEvents", "catalogue");
                });

            modelBuilder.Entity("Kairo.Modules.Catalogue.Products.Product", b =>
                {
                    b.OwnsOne("Kairo.Modules.Catalogue.Products.ProductFixes", "Fixes", b1 =>
                        {
                            b1.Property<Guid>("ProductId");

                            b1.Property<string>("Category")
                                .HasJsonPropertyName("category");

                            b1.Property<string>("Currency")
                                .HasJsonPropertyName("currency");

                            b1.Property<string>("ImageLink")
                                .HasJsonPropertyName("imageLink");

                            b1.Property<string>("Link")
                                .HasJsonPropertyName("link");

                            b1.Property<decimal?>("Price")
                                .HasJsonPropertyName("price");

                            b1.Property<string>("Title")
                                .HasJsonPropertyName("title");

                            b1.HasKey("ProductId");

                            b1.ToTable("Products", "catalogue");

                            b1
                                .ToJson("Fixes")
                                .HasColumnType("nvarchar(max)");

                            b1.WithOwner()
                                .HasForeignKey("ProductId");
                        });

                    b.Navigation("Fixes")
                        .IsRequired();
                });
#pragma warning restore 612, 618
        }
    }
}
