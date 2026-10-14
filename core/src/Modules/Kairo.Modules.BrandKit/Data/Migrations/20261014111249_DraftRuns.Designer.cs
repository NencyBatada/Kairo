using System;
using Kairo.Modules.BrandKit.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Kairo.Modules.BrandKit.Data.Migrations
{
    [DbContext(typeof(BrandKitDbContext))]
    [Migration("20261014111249_DraftRuns")]
    partial class DraftRuns
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasDefaultSchema("brand_kit")
                .HasAnnotation("ProductVersion", "10.0.12")
                .HasAnnotation("Relational:MaxIdentifierLength", 128);

            SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder);

            modelBuilder.Entity("Kairo.Modules.BrandKit.Drafting.DraftRun", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Ads")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<int>("Attempts")
                        .HasColumnType("int");

                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset?>("ClaimedUntil")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("ErrorCode")
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<string>("ErrorMessage")
                        .HasMaxLength(500)
                        .HasColumnType("nvarchar(500)");

                    b.Property<DateTimeOffset?>("FinishedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Model")
                        .HasMaxLength(100)
                        .HasColumnType("nvarchar(100)");

                    b.Property<string>("Pages")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("PromptVersion")
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<DateTimeOffset>("RequestedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid>("RequestedBy")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset?>("StartedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<int?>("VersionNumber")
                        .HasColumnType("int");

                    b.Property<string>("WebsiteUrl")
                        .IsRequired()
                        .HasMaxLength(2000)
                        .HasColumnType("nvarchar(2000)");

                    b.HasKey("Id");

                    b.HasIndex("BrandId", "RequestedAt");

                    b.HasIndex("Status", "ClaimedUntil");

                    b.HasIndex(new[] { "BrandId" }, "IX_DraftRuns_OneInProgress")
                        .IsUnique()
                        .HasFilter("[Status] IN (N'Pending', N'Running')");

                    b.ToTable("DraftRuns", "brand_kit");
                });

            modelBuilder.Entity("Kairo.Modules.BrandKit.Files.KitFile", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("ContentType")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("nvarchar(100)");

                    b.Property<DateTimeOffset?>("DeletedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("FileName")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<string>("Kind")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<long>("Size")
                        .HasColumnType("bigint");

                    b.Property<string>("StorageKey")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<DateTimeOffset>("UploadedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid>("UploadedBy")
                        .HasColumnType("uniqueidentifier");

                    b.HasKey("Id");

                    b.HasIndex("BrandId", "UploadedAt");

                    b.ToTable("KitFiles", "brand_kit");
                });

            modelBuilder.Entity("Kairo.Modules.BrandKit.Kit.KitVersion", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset?>("ApprovedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid?>("ApprovedBy")
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Content")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid>("CreatedBy")
                        .HasColumnType("uniqueidentifier");

                    b.Property<int>("Number")
                        .HasColumnType("int");

                    b.Property<string>("Origin")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<int?>("RestoredFrom")
                        .HasColumnType("int");

                    b.Property<int>("Revision")
                        .HasColumnType("int");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<DateTimeOffset>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid>("UpdatedBy")
                        .HasColumnType("uniqueidentifier");

                    b.HasKey("Id");

                    b.HasIndex("BrandId", "Number")
                        .IsUnique();

                    b.HasIndex(new[] { "BrandId" }, "IX_KitVersions_OneApproved")
                        .IsUnique()
                        .HasFilter("[Status] = N'Approved'");

                    b.HasIndex(new[] { "BrandId" }, "IX_KitVersions_OneDraft")
                        .IsUnique()
                        .HasFilter("[Status] = N'Draft'");

                    b.ToTable("KitVersions", "brand_kit");
                });
#pragma warning restore 612, 618
        }
    }
}
