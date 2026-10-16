using System;
using Kairo.Modules.Launch.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Kairo.Modules.Launch.Data.Migrations
{
    [DbContext(typeof(LaunchDbContext))]
    [Migration("20261016132626_CampaignPacing")]
    partial class CampaignPacing
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasDefaultSchema("launch")
                .HasAnnotation("ProductVersion", "10.0.12")
                .HasAnnotation("Relational:MaxIdentifierLength", 128);

            SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder);

            modelBuilder.Entity("Kairo.Modules.Launch.Data.AdPause", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("AdResource")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<Guid?>("BatchId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("CampaignResource")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<string>("CampaignType")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<DateTimeOffset?>("DecidedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid?>("DecidedBy")
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("LaunchId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset?>("PausedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("ProductIds")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<DateTimeOffset?>("ResumedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid>("RoomId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("State")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<int>("Version")
                        .HasColumnType("int");

                    b.HasKey("Id");

                    b.HasIndex("BrandId", "AdResource")
                        .IsUnique()
                        .HasFilter("[State] IN (N'held', N'paused')");

                    b.HasIndex("BrandId", "BatchId");

                    b.HasIndex("BrandId", "RoomId", "CreatedAt");

                    b.ToTable("AdPauses", "launch");
                });

            modelBuilder.Entity("Kairo.Modules.Launch.Data.LaunchHealth", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Alerts")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<int>("Attempts")
                        .HasColumnType("int");

                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Cause")
                        .HasMaxLength(500)
                        .HasColumnType("nvarchar(500)");

                    b.Property<DateTimeOffset?>("CheckedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid?>("ClaimToken")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset?>("ClaimedUntil")
                        .HasColumnType("datetimeoffset");

                    b.Property<DateTimeOffset>("DueAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid>("LaunchId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasMaxLength(30)
                        .HasColumnType("nvarchar(30)");

                    b.HasKey("Id");

                    b.HasIndex("LaunchId")
                        .IsUnique();

                    b.HasIndex("Status", "DueAt");

                    b.ToTable("Health", "launch");
                });

            modelBuilder.Entity("Kairo.Modules.Launch.Data.LaunchRecord", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset?>("ActivatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("AttemptingStep")
                        .HasMaxLength(100)
                        .HasColumnType("nvarchar(100)");

                    b.Property<int>("Attempts")
                        .HasColumnType("int");

                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid?>("ClaimToken")
                        .IsConcurrencyToken()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset?>("ClaimedUntil")
                        .HasColumnType("datetimeoffset");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("ErrorCode")
                        .HasMaxLength(60)
                        .HasColumnType("nvarchar(60)");

                    b.Property<string>("ErrorMessage")
                        .HasMaxLength(500)
                        .HasColumnType("nvarchar(500)");

                    b.Property<string>("Hash")
                        .IsRequired()
                        .HasMaxLength(64)
                        .HasColumnType("nvarchar(64)");

                    b.Property<DateTimeOffset?>("NextAttemptAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("PendingBuild")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Plan")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Resources")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<int>("RetryCount")
                        .HasColumnType("int");

                    b.Property<Guid>("RoomId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasMaxLength(30)
                        .HasColumnType("nvarchar(30)");

                    b.Property<string>("Step")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("nvarchar(100)");

                    b.Property<string>("SuggestedFix")
                        .HasMaxLength(500)
                        .HasColumnType("nvarchar(500)");

                    b.Property<bool>("Uncertain")
                        .HasColumnType("bit");

                    b.Property<string>("Uploads")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<int>("Version")
                        .HasColumnType("int");

                    b.HasKey("Id");

                    b.HasIndex("BrandId", "RoomId")
                        .IsUnique()
                        .HasFilter("[Status] <> N'superseded'");

                    b.HasIndex("Status", "ClaimedUntil");

                    b.ToTable("Launches", "launch");
                });

            modelBuilder.Entity("Kairo.Modules.Launch.Data.LaunchStep", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<int>("Attempt")
                        .HasColumnType("int");

                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset?>("FinishedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid>("LaunchId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("nvarchar(100)");

                    b.Property<string>("Outcome")
                        .IsRequired()
                        .HasMaxLength(30)
                        .HasColumnType("nvarchar(30)");

                    b.Property<DateTimeOffset>("StartedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("BrandId", "LaunchId", "Name", "Attempt")
                        .IsUnique();

                    b.ToTable("Steps", "launch");
                });

            modelBuilder.Entity("Kairo.Modules.Launch.Data.PacingDay", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset?>("AlertedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("CampaignType")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<string>("Cause")
                        .HasMaxLength(500)
                        .HasColumnType("nvarchar(500)");

                    b.Property<string>("CauseKind")
                        .HasMaxLength(40)
                        .HasColumnType("nvarchar(40)");

                    b.Property<long>("Clicks")
                        .HasColumnType("bigint");

                    b.Property<decimal>("ConversionValue")
                        .HasPrecision(19, 4)
                        .HasColumnType("decimal(19,4)");

                    b.Property<decimal>("Conversions")
                        .HasPrecision(19, 4)
                        .HasColumnType("decimal(19,4)");

                    b.Property<DateOnly>("Date")
                        .HasColumnType("date");

                    b.Property<decimal>("Deviation")
                        .HasPrecision(9, 4)
                        .HasColumnType("decimal(9,4)");

                    b.Property<long>("Impressions")
                        .HasColumnType("bigint");

                    b.Property<Guid>("LaunchId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<decimal>("PlannedSpend")
                        .HasPrecision(19, 4)
                        .HasColumnType("decimal(19,4)");

                    b.Property<DateTimeOffset>("RecordedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid>("RoomId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<decimal>("Spend")
                        .HasPrecision(19, 4)
                        .HasColumnType("decimal(19,4)");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<bool>("StreakAlerted")
                        .HasColumnType("bit");

                    b.Property<string>("SuggestedFix")
                        .HasMaxLength(500)
                        .HasColumnType("nvarchar(500)");

                    b.HasKey("Id");

                    b.HasIndex("BrandId", "RoomId", "Date");

                    b.HasIndex("BrandId", "LaunchId", "CampaignType", "Date")
                        .IsUnique();

                    b.ToTable("Pacing", "launch");
                });

            modelBuilder.Entity("Kairo.Modules.Launch.Data.ProductAdMap", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("AdResource")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("CampaignResource")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<string>("CampaignType")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<string>("ProductId")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<Guid>("RoomId")
                        .HasColumnType("uniqueidentifier");

                    b.HasKey("Id");

                    b.HasIndex("BrandId", "RoomId", "ProductId", "AdResource")
                        .IsUnique();

                    b.ToTable("ProductAds", "launch");
                });

            modelBuilder.Entity("Kairo.Modules.Launch.Data.StockWatch", b =>
                {
                    b.Property<Guid>("BrandId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset?>("CheckedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid?>("ClaimToken")
                        .IsConcurrencyToken()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset?>("ClaimedUntil")
                        .HasColumnType("datetimeoffset");

                    b.Property<DateTimeOffset?>("FeedUpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<DateTimeOffset>("NextCheckAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<DateTimeOffset?>("StaleSince")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("BrandId");

                    b.HasIndex("NextCheckAt");

                    b.ToTable("StockWatches", "launch");
                });
#pragma warning restore 612, 618
        }
    }
}
