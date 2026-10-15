using System;
using Kairo.Modules.Opportunities.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Kairo.Modules.Opportunities.Data.Migrations
{
    [DbContext(typeof(OpportunitiesDbContext))]
    [Migration("20261015165142_OpportunityBids")]
    partial class OpportunityBids
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasDefaultSchema("opportunities")
                .HasAnnotation("ProductVersion", "10.0.12")
                .HasAnnotation("Relational:MaxIdentifierLength", 128);

            SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder);

            modelBuilder.Entity("Kairo.Modules.Opportunities.Radar.Opportunity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<long>("AvgMonthlySearches")
                        .HasColumnType("bigint");

                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("BriefStatus")
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<DateTimeOffset?>("ClosedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid?>("ClosedBy")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("DismissNote")
                        .HasMaxLength(500)
                        .HasColumnType("nvarchar(500)");

                    b.Property<string>("DismissReason")
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<DateTimeOffset>("FirstSeenAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("FitReason")
                        .HasMaxLength(500)
                        .HasColumnType("nvarchar(500)");

                    b.Property<double>("Growth")
                        .HasColumnType("float");

                    b.Property<double?>("GrowthAtClose")
                        .HasColumnType("float");

                    b.PrimitiveCollection<string>("Keywords")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<long>("LastMonthSearches")
                        .HasColumnType("bigint");

                    b.Property<DateTimeOffset>("LastSeenAt")
                        .HasColumnType("datetimeoffset");

                    b.PrimitiveCollection<string>("MatchedCategories")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.PrimitiveCollection<string>("MatchedProductIds")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<int>("MissedRefreshes")
                        .HasColumnType("int");

                    b.Property<string>("Model")
                        .HasMaxLength(100)
                        .HasColumnType("nvarchar(100)");

                    b.Property<double?>("Mom")
                        .HasColumnType("float");

                    b.Property<string>("PromptVersion")
                        .HasMaxLength(100)
                        .HasColumnType("nvarchar(100)");

                    b.Property<string>("ReasonsStatus")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<Guid?>("RoomId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset?>("SavedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<int>("Score")
                        .HasColumnType("int");

                    b.Property<string>("Source")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<string>("Topic")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<string>("TopicKey")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<double?>("Yoy")
                        .HasColumnType("float");

                    b.HasKey("Id");

                    SqlServerKeyBuilderExtensions.IsClustered(b.HasKey("Id"), false);

                    b.HasIndex("BrandId", "FirstSeenAt");

                    SqlServerIndexBuilderExtensions.IsClustered(b.HasIndex("BrandId", "FirstSeenAt"));

                    b.HasIndex("BrandId", "Status", "Score");

                    b.ToTable("Opportunities", "opportunities");
                });

            modelBuilder.Entity("Kairo.Modules.Opportunities.Refresh.RefreshRun", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<int>("Created")
                        .HasColumnType("int");

                    b.Property<string>("Error")
                        .HasMaxLength(1000)
                        .HasColumnType("nvarchar(1000)");

                    b.Property<int>("Expired")
                        .HasColumnType("int");

                    b.Property<DateTimeOffset>("FinishedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<int>("Keywords")
                        .HasColumnType("int");

                    b.Property<string>("Outcome")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<string>("Reason")
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<DateTimeOffset>("StartedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<int>("Updated")
                        .HasColumnType("int");

                    b.HasKey("Id");

                    SqlServerKeyBuilderExtensions.IsClustered(b.HasKey("Id"), false);

                    b.HasIndex("BrandId", "StartedAt");

                    SqlServerIndexBuilderExtensions.IsClustered(b.HasIndex("BrandId", "StartedAt"));

                    b.ToTable("RefreshRuns", "opportunities");
                });

            modelBuilder.Entity("Kairo.Modules.Opportunities.Topics.WatchTopic", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid>("CreatedBy")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Kind")
                        .IsRequired()
                        .HasMaxLength(10)
                        .HasColumnType("nvarchar(10)");

                    b.Property<string>("Phrase")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("nvarchar(100)");

                    b.HasKey("Id");

                    SqlServerKeyBuilderExtensions.IsClustered(b.HasKey("Id"), false);

                    b.HasIndex("BrandId", "Phrase")
                        .IsUnique();

                    SqlServerIndexBuilderExtensions.IsClustered(b.HasIndex("BrandId", "Phrase"));

                    b.ToTable("WatchTopics", "opportunities");
                });

            modelBuilder.Entity("Kairo.Modules.Opportunities.Radar.Opportunity", b =>
                {
                    b.OwnsOne("Kairo.Modules.Opportunities.Radar.BidRange", "Bids", b1 =>
                        {
                            b1.Property<Guid>("OpportunityId");

                            b1.Property<decimal>("High");

                            b1.Property<decimal>("Low");

                            b1.HasKey("OpportunityId");

                            b1.ToTable("Opportunities", "opportunities");

                            b1
                                .ToJson("Bids")
                                .HasColumnType("nvarchar(max)");

                            b1.WithOwner()
                                .HasForeignKey("OpportunityId");
                        });

                    b.OwnsOne("Kairo.Modules.Opportunities.Radar.OpportunityBrief", "Brief", b1 =>
                        {
                            b1.Property<Guid>("OpportunityId");

                            b1.Property<string>("Audience")
                                .IsRequired();

                            b1.PrimitiveCollection<string>("Channels")
                                .IsRequired();

                            b1.Property<DateTimeOffset>("DraftedAt");

                            b1.Property<string>("Goal")
                                .IsRequired();

                            b1.Property<string>("KeyMessage")
                                .IsRequired();

                            b1.Property<string>("Model");

                            b1.Property<string>("Offer");

                            b1.PrimitiveCollection<string>("ProductIds")
                                .IsRequired();

                            b1.Property<string>("PromptVersion");

                            b1.PrimitiveCollection<string>("Regions")
                                .IsRequired();

                            b1.Property<string>("Timing")
                                .IsRequired();

                            b1.Property<string>("Title")
                                .IsRequired();

                            b1.HasKey("OpportunityId");

                            b1.ToTable("Opportunities", "opportunities");

                            b1
                                .ToJson("Brief")
                                .HasColumnType("nvarchar(max)");

                            b1.WithOwner()
                                .HasForeignKey("OpportunityId");
                        });

                    b.OwnsMany("Kairo.Modules.Opportunities.Radar.OpportunityTrigger", "Triggers", b1 =>
                        {
                            b1.Property<Guid>("OpportunityId");

                            b1.Property<int>("__synthesizedOrdinal")
                                .ValueGeneratedOnAddOrUpdate();

                            b1.Property<DateTimeOffset>("ConditionEndsAt");

                            b1.Property<bool>("Ended");

                            b1.Property<DateTimeOffset>("FiredAt");

                            b1.Property<Guid>("FiringId");

                            b1.Property<Guid>("RuleId");

                            b1.Property<string>("RuleName")
                                .IsRequired();

                            b1.Property<string>("Source")
                                .IsRequired();

                            b1.Property<string>("Summary")
                                .IsRequired();

                            b1.HasKey("OpportunityId", "__synthesizedOrdinal");

                            b1.ToTable("Opportunities", "opportunities");

                            b1
                                .ToJson("Triggers")
                                .HasColumnType("nvarchar(max)");

                            b1.WithOwner()
                                .HasForeignKey("OpportunityId");

                            b1.OwnsMany("Kairo.Modules.Opportunities.Radar.TriggerPlace", "Regions", b2 =>
                                {
                                    b2.Property<Guid>("OpportunityTriggerOpportunityId");

                                    b2.Property<int>("OpportunityTrigger__synthesizedOrdinal");

                                    b2.Property<int>("__synthesizedOrdinal")
                                        .ValueGeneratedOnAddOrUpdate();

                                    b2.Property<string>("Id");

                                    b2.Property<string>("Name")
                                        .IsRequired();

                                    b2.HasKey("OpportunityTriggerOpportunityId", "OpportunityTrigger__synthesizedOrdinal", "__synthesizedOrdinal");

                                    b2.ToTable("Opportunities", "opportunities");

                                    b2.WithOwner()
                                        .HasForeignKey("OpportunityTriggerOpportunityId", "OpportunityTrigger__synthesizedOrdinal");
                                });

                            b1.Navigation("Regions");
                        });

                    b.OwnsMany("Kairo.Modules.Opportunities.Radar.RegionDemand", "Regions", b1 =>
                        {
                            b1.Property<Guid>("OpportunityId");

                            b1.Property<int>("__synthesizedOrdinal")
                                .ValueGeneratedOnAddOrUpdate();

                            b1.Property<double?>("Growth");

                            b1.Property<string>("Id");

                            b1.Property<long>("LastMonthSearches");

                            b1.Property<string>("Name")
                                .IsRequired();

                            b1.Property<bool>("Rising");

                            b1.HasKey("OpportunityId", "__synthesizedOrdinal");

                            b1.ToTable("Opportunities", "opportunities");

                            b1
                                .ToJson("Regions")
                                .HasColumnType("nvarchar(max)");

                            b1.WithOwner()
                                .HasForeignKey("OpportunityId");
                        });

                    b.OwnsOne("Kairo.Modules.Opportunities.Radar.ScoreBreakdown", "Breakdown", b1 =>
                        {
                            b1.Property<Guid>("OpportunityId");

                            b1.Property<double>("Feedback");

                            b1.PrimitiveCollection<string>("FeedbackReasons")
                                .IsRequired();

                            b1.Property<double>("Fit");

                            b1.Property<int>("FitScore");

                            b1.Property<int>("GrowthScore");

                            b1.Property<int>("VolumeScore");

                            b1.HasKey("OpportunityId");

                            b1.ToTable("Opportunities", "opportunities");

                            b1
                                .ToJson("Breakdown")
                                .HasColumnType("nvarchar(max)");

                            b1.WithOwner()
                                .HasForeignKey("OpportunityId");
                        });

                    b.OwnsMany("Kairo.Modules.Opportunities.Radar.SourcedReason", "Reasons", b1 =>
                        {
                            b1.Property<Guid>("OpportunityId");

                            b1.Property<int>("__synthesizedOrdinal")
                                .ValueGeneratedOnAddOrUpdate();

                            b1.Property<string>("Text")
                                .IsRequired();

                            b1.HasKey("OpportunityId", "__synthesizedOrdinal");

                            b1.ToTable("Opportunities", "opportunities");

                            b1
                                .ToJson("Reasons")
                                .HasColumnType("nvarchar(max)");

                            b1.WithOwner()
                                .HasForeignKey("OpportunityId");

                            b1.OwnsMany("Kairo.Modules.Opportunities.Radar.ReasonSource", "Sources", b2 =>
                                {
                                    b2.Property<Guid>("SourcedReasonOpportunityId");

                                    b2.Property<int>("SourcedReason__synthesizedOrdinal");

                                    b2.Property<int>("__synthesizedOrdinal")
                                        .ValueGeneratedOnAddOrUpdate();

                                    b2.Property<string>("Title")
                                        .IsRequired();

                                    b2.Property<string>("Url")
                                        .IsRequired();

                                    b2.HasKey("SourcedReasonOpportunityId", "SourcedReason__synthesizedOrdinal", "__synthesizedOrdinal");

                                    b2.ToTable("Opportunities", "opportunities");

                                    b2.WithOwner()
                                        .HasForeignKey("SourcedReasonOpportunityId", "SourcedReason__synthesizedOrdinal");
                                });

                            b1.Navigation("Sources");
                        });

                    b.OwnsMany("Kairo.Modules.Opportunities.Radar.TrendPoint", "Trend", b1 =>
                        {
                            b1.Property<Guid>("OpportunityId");

                            b1.Property<int>("__synthesizedOrdinal")
                                .ValueGeneratedOnAddOrUpdate();

                            b1.Property<int>("Month");

                            b1.Property<long>("Searches");

                            b1.Property<int>("Year");

                            b1.HasKey("OpportunityId", "__synthesizedOrdinal");

                            b1.ToTable("Opportunities", "opportunities");

                            b1
                                .ToJson("Trend")
                                .HasColumnType("nvarchar(max)");

                            b1.WithOwner()
                                .HasForeignKey("OpportunityId");
                        });

                    b.Navigation("Bids");

                    b.Navigation("Breakdown")
                        .IsRequired();

                    b.Navigation("Brief");

                    b.Navigation("Reasons");

                    b.Navigation("Regions");

                    b.Navigation("Trend");

                    b.Navigation("Triggers");
                });
#pragma warning restore 612, 618
        }
    }
}
