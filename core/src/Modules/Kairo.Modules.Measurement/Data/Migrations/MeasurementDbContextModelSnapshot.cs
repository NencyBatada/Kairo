using System;
using Kairo.Modules.Measurement.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Kairo.Modules.Measurement.Data.Migrations
{
    [DbContext(typeof(MeasurementDbContext))]
    partial class MeasurementDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasDefaultSchema("measurement")
                .HasAnnotation("ProductVersion", "10.0.12")
                .HasAnnotation("Relational:MaxIdentifierLength", 128);

            SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder);

            modelBuilder.Entity("Kairo.Modules.Measurement.Feeds.FeedRun", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Error")
                        .HasMaxLength(1000)
                        .HasColumnType("nvarchar(1000)");

                    b.Property<string>("ExternalId")
                        .HasMaxLength(300)
                        .HasColumnType("nvarchar(300)");

                    b.Property<string>("Feed")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<DateTimeOffset?>("FinishedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<DateOnly?>("FromDate")
                        .HasColumnType("date");

                    b.Property<string>("Reason")
                        .HasMaxLength(40)
                        .HasColumnType("nvarchar(40)");

                    b.Property<int>("Rows")
                        .HasColumnType("int");

                    b.Property<DateTimeOffset>("StartedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<string>("Tables")
                        .HasMaxLength(300)
                        .HasColumnType("nvarchar(300)");

                    b.Property<DateOnly?>("ToDate")
                        .HasColumnType("date");

                    b.HasKey("Id");

                    b.HasIndex("StartedAt");

                    b.HasIndex("BrandId", "Feed", "StartedAt");

                    b.ToTable("FeedRuns", "measurement");
                });

            modelBuilder.Entity("Kairo.Modules.Measurement.Feeds.SalesSource", b =>
                {
                    b.Property<Guid>("BrandId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("DateColumn")
                        .IsRequired()
                        .HasMaxLength(300)
                        .HasColumnType("nvarchar(300)");

                    b.Property<string>("DateColumnType")
                        .IsRequired()
                        .ValueGeneratedOnAdd()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)")
                        .HasDefaultValue("DATE");

                    b.Property<string>("Location")
                        .IsRequired()
                        .HasMaxLength(64)
                        .HasColumnType("nvarchar(64)");

                    b.Property<string>("OrdersColumn")
                        .IsRequired()
                        .HasMaxLength(300)
                        .HasColumnType("nvarchar(300)");

                    b.Property<string>("RegionColumn")
                        .IsRequired()
                        .HasMaxLength(300)
                        .HasColumnType("nvarchar(300)");

                    b.Property<string>("RevenueColumn")
                        .IsRequired()
                        .HasMaxLength(300)
                        .HasColumnType("nvarchar(300)");

                    b.Property<DateTimeOffset>("SetAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid>("SetBy")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("TableRef")
                        .IsRequired()
                        .HasMaxLength(300)
                        .HasColumnType("nvarchar(300)");

                    b.HasKey("BrandId");

                    b.ToTable("SalesSources", "measurement");
                });

            modelBuilder.Entity("Kairo.Modules.Measurement.Feeds.SalesUpload", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<int>("Days")
                        .HasColumnType("int");

                    b.Property<string>("FileName")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<DateOnly>("FirstDate")
                        .HasColumnType("date");

                    b.Property<DateOnly>("LastDate")
                        .HasColumnType("date");

                    b.Property<int>("Rows")
                        .HasColumnType("int");

                    b.Property<DateTimeOffset>("UploadedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid>("UploadedBy")
                        .HasColumnType("uniqueidentifier");

                    b.HasKey("Id");

                    b.HasIndex("BrandId", "UploadedAt");

                    b.ToTable("SalesUploads", "measurement");
                });

            modelBuilder.Entity("Kairo.Modules.Measurement.Impact.ImpactReport", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<int>("Attempts")
                        .HasColumnType("int");

                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<decimal?>("CostPerExtra")
                        .HasPrecision(19, 4)
                        .HasColumnType("decimal(19,4)");

                    b.Property<decimal?>("CostPerExtraHigh")
                        .HasPrecision(19, 4)
                        .HasColumnType("decimal(19,4)");

                    b.Property<decimal?>("CostPerExtraLow")
                        .HasPrecision(19, 4)
                        .HasColumnType("decimal(19,4)");

                    b.Property<decimal?>("CostPerResult")
                        .HasPrecision(19, 4)
                        .HasColumnType("decimal(19,4)");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Currency")
                        .IsRequired()
                        .HasMaxLength(3)
                        .HasColumnType("nvarchar(3)");

                    b.Property<string>("Flags")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("InconclusiveReason")
                        .HasMaxLength(800)
                        .HasColumnType("nvarchar(800)");

                    b.Property<string>("Kpi")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<string>("Model")
                        .HasMaxLength(80)
                        .HasColumnType("nvarchar(80)");

                    b.Property<string>("NextStep")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<string>("NextStepReasoning")
                        .HasMaxLength(1000)
                        .HasColumnType("nvarchar(1000)");

                    b.Property<string>("NextTest")
                        .HasMaxLength(1000)
                        .HasColumnType("nvarchar(1000)");

                    b.Property<string>("NextTestAdvice")
                        .HasMaxLength(800)
                        .HasColumnType("nvarchar(800)");

                    b.Property<string>("Outcome")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<string>("Problem")
                        .HasMaxLength(1000)
                        .HasColumnType("nvarchar(1000)");

                    b.Property<string>("PromptVersion")
                        .HasMaxLength(80)
                        .HasColumnType("nvarchar(80)");

                    b.Property<Guid>("ReadingId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset?>("ReadyAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<decimal>("Results")
                        .HasPrecision(19, 4)
                        .HasColumnType("decimal(19,4)");

                    b.Property<Guid>("RoomId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<decimal>("Spend")
                        .HasPrecision(19, 4)
                        .HasColumnType("decimal(19,4)");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<string>("Summary")
                        .HasMaxLength(1200)
                        .HasColumnType("nvarchar(1200)");

                    b.Property<decimal?>("TargetCost")
                        .HasPrecision(19, 4)
                        .HasColumnType("decimal(19,4)");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<DateTimeOffset>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<int>("Version")
                        .IsConcurrencyToken()
                        .HasColumnType("int");

                    b.HasKey("Id");

                    b.HasIndex("BrandId", "RoomId")
                        .IsUnique();

                    b.HasIndex("BrandId", "Status", "ReadyAt");

                    b.ToTable("ImpactReports", "measurement");
                });

            modelBuilder.Entity("Kairo.Modules.Measurement.Lift.LiftReading", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<double?>("Actual")
                        .HasColumnType("float");

                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<bool>("Conclusive")
                        .HasColumnType("bit");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Daily")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<int>("Days")
                        .HasColumnType("int");

                    b.Property<bool>("Early")
                        .HasColumnType("bit");

                    b.Property<double?>("Expected")
                        .HasColumnType("float");

                    b.Property<double?>("Extra")
                        .HasColumnType("float");

                    b.Property<double?>("ExtraHigh")
                        .HasColumnType("float");

                    b.Property<double?>("ExtraLow")
                        .HasColumnType("float");

                    b.Property<double?>("High")
                        .HasColumnType("float");

                    b.Property<string>("Kpi")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<double?>("Lift")
                        .HasColumnType("float");

                    b.Property<double?>("Low")
                        .HasColumnType("float");

                    b.Property<string>("Method")
                        .IsRequired()
                        .HasMaxLength(40)
                        .HasColumnType("nvarchar(40)");

                    b.Property<int>("Placebos")
                        .HasColumnType("int");

                    b.Property<int>("PlannedDays")
                        .HasColumnType("int");

                    b.Property<string>("Reason")
                        .HasMaxLength(40)
                        .HasColumnType("nvarchar(40)");

                    b.Property<string>("Regions")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Revenue")
                        .HasColumnType("nvarchar(max)");

                    b.Property<Guid>("RoomId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<double?>("Spread")
                        .HasColumnType("float");

                    b.Property<DateOnly>("StartDate")
                        .HasColumnType("date");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<DateOnly>("ThroughDate")
                        .HasColumnType("date");

                    b.Property<string>("Unit")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.HasKey("Id");

                    b.HasIndex("BrandId", "RoomId", "Early", "ThroughDate")
                        .IsUnique();

                    b.ToTable("LiftReadings", "measurement");
                });
#pragma warning restore 612, 618
        }
    }
}
