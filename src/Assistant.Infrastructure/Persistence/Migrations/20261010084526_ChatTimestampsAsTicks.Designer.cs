using System;
using Assistant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Assistant.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AssistantDbContext))]
    [Migration("20261010084526_ChatTimestampsAsTicks")]
    partial class ChatTimestampsAsTicks
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "10.0.12");

            modelBuilder.Entity("Assistant.Domain.AppUser", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("TEXT");

                    b.Property<string>("CalendarToken")
                        .IsRequired()
                        .HasMaxLength(64)
                        .HasColumnType("TEXT");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("TEXT");

                    b.Property<string>("LinkToken")
                        .HasMaxLength(64)
                        .HasColumnType("TEXT");

                    b.Property<DateTimeOffset?>("LinkTokenExpiresAt")
                        .HasColumnType("TEXT");

                    b.Property<string>("TimeZoneId")
                        .IsRequired()
                        .HasMaxLength(64)
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("CalendarToken")
                        .IsUnique();

                    b.HasIndex("LinkToken");

                    b.ToTable("Users");
                });

            modelBuilder.Entity("Assistant.Domain.CalendarSubscription", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("TEXT");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("TEXT");

                    b.Property<string>("LastError")
                        .HasMaxLength(300)
                        .HasColumnType("TEXT");

                    b.Property<DateTimeOffset?>("LastSyncedAt")
                        .HasColumnType("TEXT");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(120)
                        .HasColumnType("TEXT");

                    b.Property<string>("Url")
                        .IsRequired()
                        .HasMaxLength(2048)
                        .HasColumnType("TEXT");

                    b.Property<Guid>("UserId")
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("UserId");

                    b.ToTable("CalendarSubscriptions");
                });

            modelBuilder.Entity("Assistant.Domain.ChatMessage", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("TEXT");

                    b.Property<string>("ConversationId")
                        .IsRequired()
                        .HasMaxLength(64)
                        .HasColumnType("TEXT");

                    b.Property<long>("CreatedAt")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Kind")
                        .HasMaxLength(16)
                        .HasColumnType("TEXT");

                    b.Property<string>("Payload")
                        .HasColumnType("TEXT");

                    b.Property<string>("Role")
                        .IsRequired()
                        .HasMaxLength(16)
                        .HasColumnType("TEXT");

                    b.Property<string>("Text")
                        .IsRequired()
                        .HasMaxLength(4000)
                        .HasColumnType("TEXT");

                    b.Property<Guid>("UserId")
                        .HasColumnType("TEXT");

                    b.Property<string>("Via")
                        .HasMaxLength(16)
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("UserId", "ConversationId", "CreatedAt");

                    b.ToTable("ChatMessages");
                });

            modelBuilder.Entity("Assistant.Domain.Conversation", b =>
                {
                    b.Property<Guid>("UserId")
                        .HasColumnType("TEXT");

                    b.Property<string>("Id")
                        .HasMaxLength(64)
                        .HasColumnType("TEXT");

                    b.Property<long>("CreatedAt")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasMaxLength(80)
                        .HasColumnType("TEXT");

                    b.Property<long>("UpdatedAt")
                        .HasColumnType("INTEGER");

                    b.HasKey("UserId", "Id");

                    b.HasIndex("UserId", "UpdatedAt");

                    b.ToTable("Conversations");
                });

            modelBuilder.Entity("Assistant.Domain.ScheduleItem", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("TEXT");

                    b.PrimitiveCollection<string>("CompletedOccurrences")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("TEXT");

                    b.Property<DateOnly?>("Date")
                        .HasColumnType("TEXT");

                    b.Property<TimeSpan?>("Duration")
                        .HasColumnType("TEXT");

                    b.Property<byte[]>("Embedding")
                        .HasColumnType("BLOB");

                    b.Property<string>("ExternalSource")
                        .HasMaxLength(64)
                        .HasColumnType("TEXT");

                    b.Property<string>("ExternalUid")
                        .HasMaxLength(512)
                        .HasColumnType("TEXT");

                    b.Property<string>("Recurrence")
                        .HasMaxLength(128)
                        .HasColumnType("TEXT");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasMaxLength(16)
                        .HasColumnType("TEXT");

                    b.Property<TimeOnly?>("Time")
                        .HasColumnType("TEXT");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("TEXT");

                    b.Property<string>("Type")
                        .IsRequired()
                        .HasMaxLength(16)
                        .HasColumnType("TEXT");

                    b.Property<DateTimeOffset>("UpdatedAt")
                        .HasColumnType("TEXT");

                    b.Property<Guid>("UserId")
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("UserId", "Date");

                    b.HasIndex("UserId", "Status");

                    b.HasIndex("UserId", "ExternalSource", "ExternalUid");

                    b.ToTable("ScheduleItems");
                });
#pragma warning restore 612, 618
        }
    }
}
