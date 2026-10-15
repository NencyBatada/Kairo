using System;
using Kairo.Modules.Measurement.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Kairo.Modules.Measurement.Data.Migrations
{
    [DbContext(typeof(MeasurementDbContext))]
    [Migration("20261015165054_FeedRunTables")]
    partial class FeedRunTables
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
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
#pragma warning restore 612, 618
        }
    }
}
