using Assistant.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Assistant.Infrastructure.Persistence;

public sealed class AssistantDbContext(DbContextOptions<AssistantDbContext> options) : DbContext(options)
{
    public DbSet<ScheduleItem> ScheduleItems => Set<ScheduleItem>();

    public DbSet<AppUser> Users => Set<AppUser>();

    public DbSet<CalendarSubscription> CalendarSubscriptions => Set<CalendarSubscription>();

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ScheduleItem>(item =>
        {
            item.HasKey(entity => entity.Id);
            item.HasIndex(entity => new { entity.UserId, entity.Date });
            item.HasIndex(entity => new { entity.UserId, entity.Status });

            item.HasIndex(entity => new { entity.UserId, entity.ExternalSource, entity.ExternalUid });
            item.Property(entity => entity.Title).HasMaxLength(ScheduleItem.MaxTitleLength);
            item.Property(entity => entity.ExternalSource).HasMaxLength(ScheduleItem.MaxExternalSourceLength);
            item.Property(entity => entity.ExternalUid).HasMaxLength(ScheduleItem.MaxExternalUidLength);
            item.Property(entity => entity.Type).HasConversion<string>().HasMaxLength(16);
            item.Property(entity => entity.Status).HasConversion<string>().HasMaxLength(16);

            item.Property(entity => entity.Recurrence)
                .HasConversion(recurrence => recurrence!.ToRRule(), rrule => Recurrence.FromRRule(rrule))
                .HasMaxLength(128);

            item.Property(entity => entity.Embedding)
                .HasConversion(
                    vector => VectorToBytes(vector!),
                    bytes => BytesToVector(bytes),
                    new ValueComparer<float[]?>(
                        (left, right) => left == null ? right == null : right != null && left.SequenceEqual(right),
                        vector => vector == null ? 0 : vector.Length,
                        vector => vector == null ? null : vector.ToArray()));

            item.PrimitiveCollection(entity => entity.CompletedOccurrences)
                .HasField("_completedOccurrences")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<CalendarSubscription>(subscription =>
        {
            subscription.HasKey(entity => entity.Id);
            subscription.HasIndex(entity => entity.UserId);
            subscription.Property(entity => entity.Url).HasMaxLength(CalendarSubscription.MaxUrlLength);
            subscription.Property(entity => entity.Name).HasMaxLength(CalendarSubscription.MaxNameLength);
            subscription.Property(entity => entity.LastError).HasMaxLength(CalendarSubscription.MaxErrorLength);
        });

        modelBuilder.Entity<Conversation>(conversation =>
        {
            conversation.HasKey(entity => new { entity.UserId, entity.Id });
            conversation.HasIndex(entity => new { entity.UserId, entity.UpdatedAt });
            conversation.Property(entity => entity.Id).HasMaxLength(Conversation.MaxIdLength);
            conversation.Property(entity => entity.Title).HasMaxLength(Conversation.MaxTitleLength);
            conversation.Property(entity => entity.CreatedAt).HasConversion<DateTimeOffsetToBinaryConverter>();
            conversation.Property(entity => entity.UpdatedAt).HasConversion<DateTimeOffsetToBinaryConverter>();
        });

        modelBuilder.Entity<ChatMessage>(message =>
        {
            message.HasKey(entity => entity.Id);
            message.HasIndex(entity => new { entity.UserId, entity.ConversationId, entity.CreatedAt });
            message.Property(entity => entity.ConversationId).HasMaxLength(Conversation.MaxIdLength);
            message.Property(entity => entity.Role).HasConversion<string>().HasMaxLength(16);
            message.Property(entity => entity.Text).HasMaxLength(ChatMessage.MaxTextLength);
            message.Property(entity => entity.Via).HasMaxLength(ChatMessage.MaxTagLength);
            message.Property(entity => entity.Kind).HasMaxLength(ChatMessage.MaxTagLength);
            message.Property(entity => entity.CreatedAt).HasConversion<DateTimeOffsetToBinaryConverter>();
        });

        modelBuilder.Entity<AppUser>(user =>
        {
            user.HasKey(entity => entity.Id);
            user.HasIndex(entity => entity.CalendarToken).IsUnique();
            user.HasIndex(entity => entity.LinkToken);
            user.Property(entity => entity.TimeZoneId).HasMaxLength(64);
            user.Property(entity => entity.CalendarToken).HasMaxLength(64);
            user.Property(entity => entity.LinkToken).HasMaxLength(64);
        });
    }

    private static byte[] VectorToBytes(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] BytesToVector(byte[] bytes)
    {
        var vector = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
        return vector;
    }
}
