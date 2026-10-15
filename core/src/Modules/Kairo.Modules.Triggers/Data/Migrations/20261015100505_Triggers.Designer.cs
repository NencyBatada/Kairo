using System;
using Kairo.Modules.Triggers.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Kairo.Modules.Triggers.Data.Migrations
{
    [DbContext(typeof(TriggersDbContext))]
    [Migration("20261015100505_Triggers")]
    partial class Triggers
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasDefaultSchema("triggers")
                .HasAnnotation("ProductVersion", "10.0.12")
                .HasAnnotation("Relational:MaxIdentifierLength", 128);

            SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder);

            modelBuilder.Entity("Kairo.Modules.Triggers.Checks.TriggerFiring", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("ConditionEndsAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<DateTimeOffset>("CooldownUntil")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Details")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<DateTimeOffset?>("EndedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<DateTimeOffset>("FiredAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid?>("OpportunityId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Outcome")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.PrimitiveCollection<string>("Regions")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<Guid>("RuleId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Summary")
                        .IsRequired()
                        .HasMaxLength(500)
                        .HasColumnType("nvarchar(500)");

                    b.HasKey("Id");

                    SqlServerKeyBuilderExtensions.IsClustered(b.HasKey("Id"), false);

                    b.HasIndex("BrandId", "FiredAt");

                    SqlServerIndexBuilderExtensions.IsClustered(b.HasIndex("BrandId", "FiredAt"));

                    b.HasIndex("BrandId", "RuleId", "FiredAt");

                    b.ToTable("TriggerFirings", "triggers");
                });

            modelBuilder.Entity("Kairo.Modules.Triggers.Festivals.BrandTriggerState", b =>
                {
                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("FestivalsSeededAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("BrandId");

                    b.ToTable("BrandStates", "triggers");
                });

            modelBuilder.Entity("Kairo.Modules.Triggers.Festivals.FestivalEvent", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid?>("CreatedBy")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateOnly>("EndsOn")
                        .HasColumnType("date");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(80)
                        .HasColumnType("nvarchar(80)");

                    b.Property<string>("Origin")
                        .IsRequired()
                        .HasMaxLength(10)
                        .HasColumnType("nvarchar(10)");

                    b.PrimitiveCollection<string>("Regions")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<DateOnly>("StartsOn")
                        .HasColumnType("date");

                    b.HasKey("Id");

                    SqlServerKeyBuilderExtensions.IsClustered(b.HasKey("Id"), false);

                    b.HasIndex("BrandId", "StartsOn");

                    SqlServerIndexBuilderExtensions.IsClustered(b.HasIndex("BrandId", "StartsOn"));

                    b.ToTable("FestivalEvents", "triggers");
                });

            modelBuilder.Entity("Kairo.Modules.Triggers.Rules.TriggerRule", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Category")
                        .IsRequired()
                        .HasMaxLength(750)
                        .HasColumnType("nvarchar(750)");

                    b.Property<int>("CooldownHours")
                        .HasColumnType("int");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid?>("CreatedBy")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Evidence")
                        .HasMaxLength(1000)
                        .HasColumnType("nvarchar(1000)");

                    b.Property<string>("LastCheckOutcome")
                        .HasMaxLength(30)
                        .HasColumnType("nvarchar(30)");

                    b.Property<DateTimeOffset?>("LastCheckedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<DateTimeOffset?>("LastFiredAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid?>("LastImportId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid?>("MemoryEntryId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(80)
                        .HasColumnType("nvarchar(80)");

                    b.Property<string>("Origin")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.PrimitiveCollection<string>("Regions")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Source")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<DateTimeOffset>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid?>("UpdatedBy")
                        .HasColumnType("uniqueidentifier");

                    b.HasKey("Id");

                    SqlServerKeyBuilderExtensions.IsClustered(b.HasKey("Id"), false);

                    b.HasIndex("BrandId", "CreatedAt");

                    SqlServerIndexBuilderExtensions.IsClustered(b.HasIndex("BrandId", "CreatedAt"));

                    b.HasIndex("BrandId", "Status");

                    b.ToTable("TriggerRules", "triggers");
                });

            modelBuilder.Entity("Kairo.Modules.Triggers.Weather.WeatherForecast", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("FetchedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<DateTimeOffset>("Hour")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("RegionId")
                        .IsRequired()
                        .HasMaxLength(60)
                        .HasColumnType("nvarchar(60)");

                    b.HasKey("Id");

                    SqlServerKeyBuilderExtensions.IsClustered(b.HasKey("Id"), false);

                    b.HasIndex("RegionId", "Hour")
                        .IsUnique();

                    SqlServerIndexBuilderExtensions.IsClustered(b.HasIndex("RegionId", "Hour"));

                    b.ToTable("WeatherForecasts", "triggers");
                });

            modelBuilder.Entity("Kairo.Modules.Triggers.Rules.TriggerRule", b =>
                {
                    b.OwnsOne("Kairo.Modules.Triggers.Rules.RuleCondition", "Condition", b1 =>
                        {
                            b1.Property<Guid>("TriggerRuleId");

                            b1.Property<int?>("DaysBefore");

                            b1.Property<Guid?>("EventId");

                            b1.Property<string>("Kind")
                                .HasMaxLength(20);

                            b1.Property<long?>("MinUnits");

                            b1.Property<double?>("Threshold");

                            b1.Property<int?>("WithinDays");

                            b1.HasKey("TriggerRuleId");

                            b1.ToTable("TriggerRules", "triggers");

                            b1
                                .ToJson("Condition")
                                .HasColumnType("nvarchar(max)");

                            b1.WithOwner()
                                .HasForeignKey("TriggerRuleId");
                        });

                    b.Navigation("Condition")
                        .IsRequired();
                });

            modelBuilder.Entity("Kairo.Modules.Triggers.Weather.WeatherForecast", b =>
                {
                    b.OwnsMany("Kairo.Modules.Triggers.Weather.ForecastDay", "Days", b1 =>
                        {
                            b1.Property<Guid>("WeatherForecastId");

                            b1.Property<int>("__synthesizedOrdinal")
                                .ValueGeneratedOnAddOrUpdate();

                            b1.Property<DateOnly>("Date");

                            b1.Property<double?>("MaxCelsius");

                            b1.Property<double>("RainMillimetres");

                            b1.HasKey("WeatherForecastId", "__synthesizedOrdinal");

                            b1.ToTable("WeatherForecasts", "triggers");

                            b1
                                .ToJson("Days")
                                .HasColumnType("nvarchar(max)");

                            b1.WithOwner()
                                .HasForeignKey("WeatherForecastId");
                        });

                    b.Navigation("Days");
                });
#pragma warning restore 612, 618
        }
    }
}
