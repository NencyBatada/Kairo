using System;
using Kairo.Modules.Operations.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Kairo.Modules.Operations.Data.Migrations
{
    [DbContext(typeof(OperationsDbContext))]
    [Migration("20261018084921_UsageLedger")]
    partial class UsageLedger
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasDefaultSchema("operations")
                .HasAnnotation("ProductVersion", "10.0.12")
                .HasAnnotation("Relational:MaxIdentifierLength", 128);

            SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder);

            modelBuilder.Entity("Kairo.Modules.Operations.Data.Invocation", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid?>("CampaignId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<bool>("Closed")
                        .HasColumnType("bit");

                    b.Property<DateTimeOffset>("ExpiresAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Operation")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("nvarchar(100)");

                    b.Property<Guid?>("RunId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("TokenHash")
                        .IsRequired()
                        .HasMaxLength(64)
                        .HasColumnType("nvarchar(64)");

                    b.HasKey("Id");

                    b.ToTable("Invocations", "operations");
                });

            modelBuilder.Entity("Kairo.Modules.Operations.Data.ProviderCall", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<long?>("CachedTokens")
                        .HasColumnType("bigint");

                    b.Property<Guid?>("CampaignId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<decimal?>("EstimatedUsd")
                        .HasPrecision(28, 12)
                        .HasColumnType("decimal(28,12)");

                    b.Property<int?>("GeneratedImages")
                        .HasColumnType("int");

                    b.Property<int>("Images")
                        .HasColumnType("int");

                    b.Property<long?>("InputTokens")
                        .HasColumnType("bigint");

                    b.Property<Guid>("InvocationId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Model")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("nvarchar(100)");

                    b.Property<string>("Operation")
                        .IsRequired()
                        .HasMaxLength(40)
                        .HasColumnType("nvarchar(40)");

                    b.Property<string>("Outcome")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<long?>("OutputTokens")
                        .HasColumnType("bigint");

                    b.Property<string>("RateVersion")
                        .HasMaxLength(100)
                        .HasColumnType("nvarchar(100)");

                    b.Property<string>("Region")
                        .IsRequired()
                        .HasMaxLength(40)
                        .HasColumnType("nvarchar(40)");

                    b.Property<string>("Report")
                        .HasColumnType("nvarchar(max)");

                    b.Property<DateTimeOffset?>("ReportedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid>("RequestId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Reservation")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("ResolvedModel")
                        .HasMaxLength(100)
                        .HasColumnType("nvarchar(100)");

                    b.Property<Guid?>("RunId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<long?>("SpeechCharacters")
                        .HasColumnType("bigint");

                    b.Property<decimal?>("SpeechSeconds")
                        .HasPrecision(18, 4)
                        .HasColumnType("decimal(18,4)");

                    b.Property<int>("TextCalls")
                        .HasColumnType("int");

                    b.Property<long?>("ThoughtTokens")
                        .HasColumnType("bigint");

                    b.Property<int>("VideoClips")
                        .HasColumnType("int");

                    b.Property<decimal?>("VideoSeconds")
                        .HasPrecision(18, 4)
                        .HasColumnType("decimal(18,4)");

                    b.HasKey("Id");

                    b.HasIndex("BrandId", "CampaignId");

                    b.HasIndex("BrandId", "InvocationId", "RequestId")
                        .IsUnique();

                    b.ToTable("ProviderCalls", "operations");
                });
#pragma warning restore 612, 618
        }
    }
}
