using System;
using Kairo.Modules.Identity.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Kairo.Modules.Identity.Data.Migrations
{
    [DbContext(typeof(IdentityDbContext))]
    [Migration("20261013055439_Invitations")]
    partial class Invitations
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasDefaultSchema("identity")
                .HasAnnotation("ProductVersion", "10.0.12")
                .HasAnnotation("Relational:MaxIdentifierLength", 128);

            SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder);

            modelBuilder.Entity("Kairo.Modules.Identity.Invitations.Invitation", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Email")
                        .IsRequired()
                        .HasMaxLength(320)
                        .HasColumnType("nvarchar(320)");

                    b.Property<DateTimeOffset>("ExpiresAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid>("InvitedBy")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset?>("RespondedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<int>("Roles")
                        .HasColumnType("int");

                    b.Property<DateTimeOffset>("SentAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.HasKey("Id");

                    b.HasIndex("Email");

                    b.HasIndex("InvitedBy");

                    b.HasIndex("BrandId", "Email")
                        .IsUnique()
                        .HasFilter("[Status] = N'Pending'");

                    b.ToTable("Invitations", "identity");
                });

            modelBuilder.Entity("Kairo.Modules.Identity.Members.BudgetLimit", b =>
                {
                    b.Property<Guid>("BrandId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<decimal?>("Amount")
                        .HasPrecision(19, 4)
                        .HasColumnType("decimal(19,4)");

                    b.Property<string>("Currency")
                        .IsRequired()
                        .HasMaxLength(3)
                        .HasColumnType("nchar(3)")
                        .IsFixedLength();

                    b.HasKey("BrandId");

                    b.ToTable("BudgetLimits", "identity");
                });

            modelBuilder.Entity("Kairo.Modules.Identity.Members.DutyAssignment", b =>
                {
                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Duty")
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<Guid>("UserId")
                        .HasColumnType("uniqueidentifier");

                    b.HasKey("BrandId", "Duty", "UserId");

                    b.HasIndex("BrandId", "UserId");

                    b.ToTable("ApprovalDuties", "identity");
                });

            modelBuilder.Entity("Kairo.Modules.Identity.Members.Membership", b =>
                {
                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("UserId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("JoinedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<int>("Roles")
                        .HasColumnType("int");

                    b.HasKey("BrandId", "UserId");

                    b.HasIndex("UserId");

                    b.ToTable("Memberships", "identity");
                });

            modelBuilder.Entity("Kairo.Modules.Identity.Sessions.Session", b =>
                {
                    b.Property<string>("Key")
                        .HasMaxLength(64)
                        .HasColumnType("nvarchar(64)");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<DateTimeOffset>("ExpiresAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<DateTimeOffset>("LastSeenAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<byte[]>("Ticket")
                        .IsRequired()
                        .HasColumnType("varbinary(max)");

                    b.Property<Guid>("UserId")
                        .HasColumnType("uniqueidentifier");

                    b.HasKey("Key");

                    b.HasIndex("UserId");

                    b.ToTable("Sessions", "identity");
                });

            modelBuilder.Entity("Kairo.Modules.Identity.Users.User", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Email")
                        .IsRequired()
                        .HasMaxLength(320)
                        .HasColumnType("nvarchar(320)");

                    b.Property<string>("GoogleSubject")
                        .IsRequired()
                        .HasMaxLength(255)
                        .HasColumnType("nvarchar(255)");

                    b.Property<DateTimeOffset>("LastSignedInAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<string>("PictureUrl")
                        .HasMaxLength(2048)
                        .HasColumnType("nvarchar(2048)");

                    b.HasKey("Id");

                    b.HasIndex("Email");

                    b.HasIndex("GoogleSubject")
                        .IsUnique();

                    b.ToTable("Users", "identity");
                });

            modelBuilder.Entity("Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int");

                    SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<int>("Id"));

                    b.Property<string>("FriendlyName")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Xml")
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("Id");

                    b.ToTable("DataProtectionKeys", "identity");
                });

            modelBuilder.Entity("Kairo.Modules.Identity.Invitations.Invitation", b =>
                {
                    b.HasOne("Kairo.Modules.Identity.Users.User", null)
                        .WithMany()
                        .HasForeignKey("InvitedBy")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();
                });

            modelBuilder.Entity("Kairo.Modules.Identity.Members.DutyAssignment", b =>
                {
                    b.HasOne("Kairo.Modules.Identity.Members.Membership", null)
                        .WithMany()
                        .HasForeignKey("BrandId", "UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Kairo.Modules.Identity.Members.Membership", b =>
                {
                    b.HasOne("Kairo.Modules.Identity.Users.User", null)
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();
                });

            modelBuilder.Entity("Kairo.Modules.Identity.Sessions.Session", b =>
                {
                    b.HasOne("Kairo.Modules.Identity.Users.User", null)
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });
#pragma warning restore 612, 618
        }
    }
}
