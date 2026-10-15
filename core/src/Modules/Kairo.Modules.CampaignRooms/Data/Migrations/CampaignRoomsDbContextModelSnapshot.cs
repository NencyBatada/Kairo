using System;
using Kairo.Modules.CampaignRooms.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Kairo.Modules.CampaignRooms.Data.Migrations
{
    [DbContext(typeof(CampaignRoomsDbContext))]
    partial class CampaignRoomsDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasDefaultSchema("campaign_rooms")
                .HasAnnotation("ProductVersion", "10.0.12")
                .HasAnnotation("Relational:MaxIdentifierLength", 128);

            SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder);

            modelBuilder.Entity("Kairo.Modules.CampaignRooms.Briefs.Brief", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Model")
                        .HasMaxLength(100)
                        .HasColumnType("nvarchar(100)");

                    b.Property<string>("Origin")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<string>("PromptVersion")
                        .HasMaxLength(100)
                        .HasColumnType("nvarchar(100)");

                    b.Property<Guid>("RoomId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid?>("StageRunId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<int>("Version")
                        .HasColumnType("int");

                    b.HasKey("Id");

                    SqlServerKeyBuilderExtensions.IsClustered(b.HasKey("Id"), false);

                    b.HasIndex("BrandId", "RoomId", "Version")
                        .IsUnique();

                    SqlServerIndexBuilderExtensions.IsClustered(b.HasIndex("BrandId", "RoomId", "Version"));

                    b.ToTable("Briefs", "campaign_rooms");
                });

            modelBuilder.Entity("Kairo.Modules.CampaignRooms.ProofPlans.ProofPlan", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<int>("BriefVersion")
                        .HasColumnType("int");

                    b.Property<bool>("ChangedAfterLock")
                        .HasColumnType("bit");

                    b.Property<string>("Content")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<DateTimeOffset?>("LockedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Method")
                        .HasMaxLength(100)
                        .HasColumnType("nvarchar(100)");

                    b.Property<Guid>("RoomId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid?>("StageRunId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<int>("Version")
                        .HasColumnType("int");

                    b.HasKey("Id");

                    SqlServerKeyBuilderExtensions.IsClustered(b.HasKey("Id"), false);

                    b.HasIndex("BrandId", "RoomId", "Version")
                        .IsUnique();

                    SqlServerIndexBuilderExtensions.IsClustered(b.HasIndex("BrandId", "RoomId", "Version"));

                    b.ToTable("ProofPlans", "campaign_rooms");
                });

            modelBuilder.Entity("Kairo.Modules.CampaignRooms.Rooms.Room", b =>
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

                    b.Property<string>("DiscardReason")
                        .HasMaxLength(500)
                        .HasColumnType("nvarchar(500)");

                    b.Property<string>("DiscardedFrom")
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<string>("Evidence")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<DateTimeOffset?>("GateRequestedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid?>("GateRequestedBy")
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("OpportunityId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("OwnerId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("PendingGate")
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<string>("RunStatus")
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<string>("State")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<DateTimeOffset>("StateEnteredAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<DateTimeOffset>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<int>("Version")
                        .HasColumnType("int");

                    b.HasKey("Id");

                    SqlServerKeyBuilderExtensions.IsClustered(b.HasKey("Id"), false);

                    b.HasIndex("BrandId", "CreatedAt");

                    SqlServerIndexBuilderExtensions.IsClustered(b.HasIndex("BrandId", "CreatedAt"));

                    b.HasIndex("BrandId", "OpportunityId")
                        .IsUnique();

                    b.HasIndex("BrandId", "State");

                    b.HasIndex("BrandId", "Status");

                    b.ToTable("Rooms", "campaign_rooms");
                });

            modelBuilder.Entity("Kairo.Modules.CampaignRooms.Stages.StageRun", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Agent")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<int>("Attempts")
                        .HasColumnType("int");

                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset?>("ClaimedUntil")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("ErrorCode")
                        .HasMaxLength(40)
                        .HasColumnType("nvarchar(40)");

                    b.Property<string>("ErrorMessage")
                        .HasMaxLength(500)
                        .HasColumnType("nvarchar(500)");

                    b.Property<DateTimeOffset?>("FinishedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Input")
                        .HasColumnType("nvarchar(max)");

                    b.Property<int?>("InputTokens")
                        .HasColumnType("int");

                    b.Property<string>("Kind")
                        .IsRequired()
                        .HasMaxLength(40)
                        .HasColumnType("nvarchar(40)");

                    b.Property<string>("Model")
                        .HasMaxLength(100)
                        .HasColumnType("nvarchar(100)");

                    b.Property<DateTimeOffset>("NextAttemptAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Output")
                        .HasColumnType("nvarchar(max)");

                    b.Property<int?>("OutputTokens")
                        .HasColumnType("int");

                    b.Property<string>("PromptVersion")
                        .HasMaxLength(100)
                        .HasColumnType("nvarchar(100)");

                    b.Property<DateTimeOffset>("QueuedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid?>("RequestedBy")
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("RoomId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Stage")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<DateTimeOffset?>("StartedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.HasKey("Id");

                    SqlServerKeyBuilderExtensions.IsClustered(b.HasKey("Id"), false);

                    b.HasIndex("Status", "NextAttemptAt");

                    b.HasIndex("BrandId", "RoomId", "QueuedAt");

                    SqlServerIndexBuilderExtensions.IsClustered(b.HasIndex("BrandId", "RoomId", "QueuedAt"));

                    b.ToTable("StageRuns", "campaign_rooms");
                });

            modelBuilder.Entity("Kairo.Modules.CampaignRooms.Briefs.Brief", b =>
                {
                    b.OwnsOne("Kairo.Modules.CampaignRooms.Briefs.BriefContent", "Content", b1 =>
                        {
                            b1.Property<Guid>("BriefId");

                            b1.Property<string>("Audience")
                                .IsRequired();

                            b1.PrimitiveCollection<string>("Channels")
                                .IsRequired();

                            b1.Property<string>("Goal")
                                .IsRequired();

                            b1.Property<string>("KeyMessage")
                                .IsRequired();

                            b1.Property<string>("Offer");

                            b1.PrimitiveCollection<string>("ProductIds")
                                .IsRequired();

                            b1.PrimitiveCollection<string>("Regions")
                                .IsRequired();

                            b1.Property<string>("Timing")
                                .IsRequired();

                            b1.Property<string>("Title")
                                .IsRequired();

                            b1.HasKey("BriefId");

                            b1.ToTable("Briefs", "campaign_rooms");

                            b1
                                .ToJson("Content")
                                .HasColumnType("nvarchar(max)");

                            b1.WithOwner()
                                .HasForeignKey("BriefId");
                        });

                    b.Navigation("Content")
                        .IsRequired();
                });

            modelBuilder.Entity("Kairo.Modules.CampaignRooms.Rooms.Room", b =>
                {
                    b.OwnsMany("Kairo.Modules.CampaignRooms.Rooms.RoomProduct", "Products", b1 =>
                        {
                            b1.Property<Guid>("RoomId");

                            b1.Property<int>("__synthesizedOrdinal")
                                .ValueGeneratedOnAddOrUpdate();

                            b1.Property<string>("Id");

                            b1.Property<string>("Title")
                                .IsRequired();

                            b1.HasKey("RoomId", "__synthesizedOrdinal");

                            b1.ToTable("Rooms", "campaign_rooms");

                            b1
                                .ToJson("Products")
                                .HasColumnType("nvarchar(max)");

                            b1.WithOwner()
                                .HasForeignKey("RoomId");
                        });

                    b.OwnsOne("Kairo.Modules.CampaignRooms.Rooms.RoomSendBack", "LastSendBack", b1 =>
                        {
                            b1.Property<Guid>("RoomId");

                            b1.Property<DateTimeOffset>("At");

                            b1.Property<Guid>("By");

                            b1.Property<string>("Comment")
                                .IsRequired();

                            b1.Property<string>("From")
                                .IsRequired();

                            b1.Property<string>("To")
                                .IsRequired();

                            b1.HasKey("RoomId");

                            b1.ToTable("Rooms", "campaign_rooms");

                            b1
                                .ToJson("LastSendBack")
                                .HasColumnType("nvarchar(max)");

                            b1.WithOwner()
                                .HasForeignKey("RoomId");
                        });

                    b.Navigation("LastSendBack");

                    b.Navigation("Products");
                });
#pragma warning restore 612, 618
        }
    }
}
