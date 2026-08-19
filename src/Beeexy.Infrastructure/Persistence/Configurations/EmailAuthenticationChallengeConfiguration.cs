using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class EmailAuthenticationChallengeConfiguration
    : IEntityTypeConfiguration<EmailAuthenticationChallenge>
{
    public void Configure(EntityTypeBuilder<EmailAuthenticationChallenge> builder)
    {
        builder.ToTable(
            "email_authentication_challenges",
            "identity",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_email_authentication_challenges_status",
                    "\"status\" IN ('pending', 'consumed', 'expired')");
                table.HasCheckConstraint(
                    "ck_email_authentication_challenges_attempt_count",
                    "\"attempt_count\" >= 0");
                table.HasCheckConstraint(
                    "ck_email_authentication_challenges_expiration",
                    "\"expires_at\" > \"created_at\"");
                table.HasCheckConstraint(
                    "ck_email_authentication_challenges_consumed",
                    "(\"status\" = 'consumed' AND \"consumed_at\" IS NOT NULL) OR " +
                    "(\"status\" <> 'consumed' AND \"consumed_at\" IS NULL)");
            });

        builder.HasKey(challenge => challenge.Id)
            .HasName("pk_email_authentication_challenges");

        builder.Property(challenge => challenge.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();

        builder.Property(challenge => challenge.Email)
            .HasColumnName("normalized_email")
            .HasConversion(email => email.Value, value => NormalizedEmail.Create(value))
            .HasMaxLength(NormalizedEmail.MaximumLength)
            .IsRequired();

        builder.Property(challenge => challenge.OtpHash)
            .HasColumnName("otp_hash")
            .HasConversion(hash => hash.Value, value => TokenHash.FromHash(value))
            .HasMaxLength(TokenHash.MaximumLength)
            .IsRequired();

        builder.Property(challenge => challenge.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(challenge => challenge.AttemptCount)
            .HasColumnName("attempt_count")
            .IsRequired();

        builder.Property(challenge => challenge.Status)
            .HasColumnName("status")
            .HasConversion(
                status => status.ToString().ToLowerInvariant(),
                value => Enum.Parse<ChallengeStatus>(value, true))
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(challenge => challenge.ConsumedAt)
            .HasColumnName("consumed_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(challenge => challenge.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(challenge => challenge.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(challenge => challenge.ExpiresAt)
            .HasFilter("\"status\" = 'pending' AND \"consumed_at\" IS NULL")
            .HasDatabaseName("ix_email_authentication_challenges_pending_expiry");
    }
}
