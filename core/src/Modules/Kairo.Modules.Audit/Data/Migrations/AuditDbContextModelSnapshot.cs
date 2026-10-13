using System;
using Kairo.Modules.Audit.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Kairo.Modules.Audit.Data.Migrations
{
    [DbContext(typeof(AuditDbContext))]
    partial class AuditDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasDefaultSchema("audit")
                .HasAnnotation("ProductVersion", "10.0.12")
                .HasAnnotation("Relational:MaxIdentifierLength", 128);

            SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder);

            modelBuilder.Entity("Kairo.Modules.Audit.Data.AuditEntry", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint");

                    SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<long>("Id"));

                    b.Property<string>("Action")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("nvarchar(100)");

                    b.Property<string>("ActorId")
                        .HasMaxLength(64)
                        .HasColumnType("nvarchar(64)");

                    b.Property<string>("ActorName")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<string>("ActorType")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("nvarchar(20)");

                    b.Property<string>("After")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Before")
                        .HasColumnType("nvarchar(max)");

                    b.Property<Guid>("BrandId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("OccurredAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("TargetId")
                        .IsRequired()
                        .HasMaxLength(64)
                        .HasColumnType("nvarchar(64)");

                    b.Property<string>("TargetType")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<string>("TraceId")
                        .HasMaxLength(32)
                        .HasColumnType("nvarchar(32)");

                    b.HasKey("Id");

                    b.HasIndex("BrandId", "Id");

                    b.ToTable("Entries", "audit", t =>
                        {
                            t.HasTrigger("Entries_PreventChanges");
                        });

                    b.HasAnnotation("SqlServer:UseSqlOutputClause", false);
                });
#pragma warning restore 612, 618
        }
    }
}
