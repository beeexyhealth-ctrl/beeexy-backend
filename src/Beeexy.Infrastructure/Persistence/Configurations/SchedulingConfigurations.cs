using Beeexy.Domain.Common;
using Beeexy.Domain.Directory;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal static class SchedulingConfiguration
{
    public const string Schema = "scheduling";

    public static PropertyBuilder<EntityId> ConfigureId<TEntity>(
        EntityTypeBuilder<TEntity> builder,
        System.Linq.Expressions.Expression<Func<TEntity, EntityId>> property,
        string columnName = "id")
        where TEntity : class
    {
        return builder.Property(property)
            .HasColumnName(columnName)
            .HasConversion(id => id.Value, value => EntityId.From(value))
            .ValueGeneratedNever();
    }

    public static void ConfigureUtc<TEntity>(
        EntityTypeBuilder<TEntity> builder,
        System.Linq.Expressions.Expression<Func<TEntity, DateTimeOffset>> property,
        string columnName)
        where TEntity : class
    {
        builder.Property(property)
            .HasColumnName(columnName)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
    }

    public static void MakeAppendOnly<TEntity>(EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        foreach (var property in builder.Metadata.GetProperties())
        {
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        }
    }
}

internal sealed class AvailabilitySlotConfiguration : IEntityTypeConfiguration<AvailabilitySlot>
{
    public void Configure(EntityTypeBuilder<AvailabilitySlot> builder)
    {
        builder.ToTable("availability_slots", SchedulingConfiguration.Schema, table =>
        {
            table.HasCheckConstraint(
                "ck_availability_slots_time_range",
                "ends_at > starts_at");
            table.HasCheckConstraint(
                "ck_availability_slots_timezone",
                "length(btrim(clinic_timezone)) > 0");
            table.HasCheckConstraint(
                "ck_availability_slots_modality",
                "modality IN ('in_person', 'virtual')");
        });
        builder.HasKey(slot => slot.Id).HasName("pk_availability_slots");
        SchedulingConfiguration.ConfigureId(builder, slot => slot.Id);
        SchedulingConfiguration.ConfigureId(builder, slot => slot.DoctorId, "doctor_id");
        SchedulingConfiguration.ConfigureId(builder, slot => slot.ClinicId, "clinic_id");
        SchedulingConfiguration.ConfigureId(
            builder,
            slot => slot.ClinicLocationId,
            "clinic_location_id");
        SchedulingConfiguration.ConfigureUtc(builder, slot => slot.StartsAt, "starts_at");
        SchedulingConfiguration.ConfigureUtc(builder, slot => slot.EndsAt, "ends_at");
        builder.Property(slot => slot.ClinicTimeZone)
            .HasColumnName("clinic_timezone")
            .HasConversion(value => value.Value, value => IanaTimeZone.Create(value))
            .HasMaxLength(IanaTimeZone.MaximumLength)
            .IsRequired();
        builder.Property(slot => slot.Modality)
            .HasColumnName("modality")
            .HasConversion(
                value => SchedulingPersistence.StoreModality(value),
                value => SchedulingPersistence.LoadModality(value))
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(slot => slot.IsPublished)
            .HasColumnName("is_published")
            .IsRequired();
        SchedulingConfiguration.ConfigureUtc(builder, slot => slot.CreatedAt, "created_at");
        builder.Property(slot => slot.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone");
        builder.Ignore(slot => slot.Duration);

        builder.HasIndex(slot => new { slot.DoctorId, slot.IsPublished, slot.StartsAt })
            .HasDatabaseName("ix_availability_slots_doctor_published_start");
        builder.HasIndex(slot => new { slot.ClinicId, slot.IsPublished, slot.StartsAt })
            .HasDatabaseName("ix_availability_slots_clinic_published_start");
        builder.HasIndex(slot => new { slot.ClinicId, slot.ClinicLocationId, slot.StartsAt })
            .HasDatabaseName("ix_availability_slots_location_start");

        builder.HasOne<Doctor>()
            .WithMany()
            .HasForeignKey(slot => slot.DoctorId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_availability_slots_doctors_doctor_id");
        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(slot => slot.ClinicId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_availability_slots_clinics_clinic_id");
        builder.HasOne<ClinicLocation>()
            .WithMany()
            .HasForeignKey(slot => new { slot.ClinicId, slot.ClinicLocationId })
            .HasPrincipalKey(location => new { location.ClinicId, location.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_availability_slots_clinic_locations");
    }
}

internal sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("appointments", SchedulingConfiguration.Schema, table =>
        {
            table.HasCheckConstraint(
                "ck_appointments_status",
                "status IN ('requested','confirmed','cancelled','completed','no_show','rejected')");
            table.HasCheckConstraint(
                "ck_appointments_modality",
                "modality IN ('in_person', 'virtual')");
            table.HasCheckConstraint(
                "ck_appointments_reason",
                "reason IS NULL OR (length(btrim(reason)) > 0 AND length(reason) <= 500)");
            table.HasCheckConstraint(
                "ck_appointments_request_fingerprint",
                "request_fingerprint ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_appointments_version", "version > 0");
        });
        builder.HasKey(appointment => appointment.Id).HasName("pk_appointments");
        SchedulingConfiguration.ConfigureId(builder, appointment => appointment.Id);
        SchedulingConfiguration.ConfigureId(
            builder,
            appointment => appointment.PatientProfileId,
            "patient_profile_id");
        SchedulingConfiguration.ConfigureId(
            builder,
            appointment => appointment.AvailabilitySlotId,
            "availability_slot_id");
        SchedulingConfiguration.ConfigureUtc(
            builder,
            appointment => appointment.ScheduledStartAt,
            "scheduled_start_at");
        SchedulingConfiguration.ConfigureId(
            builder,
            appointment => appointment.RequestingAccountId,
            "requesting_account_id");
        builder.Property(appointment => appointment.Status)
            .HasColumnName("status")
            .HasConversion(
                value => SchedulingPersistence.StoreStatus(value),
                value => SchedulingPersistence.LoadStatus(value))
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(appointment => appointment.Modality)
            .HasColumnName("modality")
            .HasConversion(
                value => SchedulingPersistence.StoreModality(value),
                value => SchedulingPersistence.LoadModality(value))
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(appointment => appointment.Reason)
            .HasColumnName("reason")
            .HasConversion(
                value => value == null ? null : value.Value,
                value => value == null ? null : AppointmentReason.Create(value))
            .HasMaxLength(AppointmentReason.MaximumLength);
        SchedulingConfiguration.ConfigureId(
            builder,
            appointment => appointment.IdempotencyKey,
            "idempotency_key");
        builder.Property(appointment => appointment.RequestFingerprint)
            .HasColumnName("request_fingerprint")
            .HasConversion(
                value => value.Value,
                value => AppointmentRequestFingerprint.Create(value))
            .HasMaxLength(AppointmentRequestFingerprint.Length)
            .IsFixedLength()
            .IsRequired();
        builder.Property(appointment => appointment.Version)
            .HasColumnName("version")
            .HasDefaultValue(1L)
            .IsConcurrencyToken()
            .IsRequired();
        SchedulingConfiguration.ConfigureUtc(
            builder,
            appointment => appointment.CreatedAt,
            "created_at");
        builder.Property(appointment => appointment.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone");
        builder.Ignore(appointment => appointment.ReservesSlot);

        builder.HasIndex(appointment => new
        {
            appointment.PatientProfileId,
            appointment.ScheduledStartAt,
            appointment.Status
        }).HasDatabaseName("ix_appointments_patient_start_status");
        builder.HasIndex(appointment => appointment.Status)
            .HasDatabaseName("ix_appointments_status");
        builder.HasIndex(appointment => new
        {
            appointment.AvailabilitySlotId,
            appointment.Status
        }).HasDatabaseName("ix_appointments_slot_status");
        builder.HasIndex(appointment => new
        {
            appointment.RequestingAccountId,
            appointment.IdempotencyKey
        })
            .IsUnique()
            .HasDatabaseName("ux_appointments_account_idempotency_key");
        builder.HasIndex(appointment => appointment.AvailabilitySlotId)
            .IsUnique()
            .HasFilter(SchedulingPersistence.ReservingAppointmentFilter)
            .HasDatabaseName("ux_appointments_reserving_slot");

        builder.HasOne<PatientProfile>()
            .WithMany()
            .HasForeignKey(appointment => appointment.PatientProfileId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_appointments_patient_profiles_patient_profile_id");
        builder.HasOne<AvailabilitySlot>()
            .WithMany()
            .HasForeignKey(appointment => appointment.AvailabilitySlotId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_appointments_availability_slots_slot_id");
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(appointment => appointment.RequestingAccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_appointments_accounts_requesting_account_id");
        builder.HasMany(appointment => appointment.StatusHistory)
            .WithOne()
            .HasForeignKey(history => history.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_appointment_status_history_appointments_appointment_id");
        builder.Navigation(appointment => appointment.StatusHistory)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class AppointmentStatusHistoryConfiguration
    : IEntityTypeConfiguration<AppointmentStatusHistory>
{
    public void Configure(EntityTypeBuilder<AppointmentStatusHistory> builder)
    {
        builder.ToTable("appointment_status_history", SchedulingConfiguration.Schema, table =>
        {
            table.HasCheckConstraint(
                "ck_appointment_status_history_previous_status",
                "previous_status IS NULL OR previous_status IN " +
                "('requested','confirmed','cancelled','completed','no_show','rejected')");
            table.HasCheckConstraint(
                "ck_appointment_status_history_new_status",
                "new_status IN ('requested','confirmed','cancelled','completed','no_show','rejected')");
            table.HasCheckConstraint(
                "ck_appointment_status_history_actor_type",
                "actor_type IN ('patient_authority','appointment_scheduler')");
            table.HasCheckConstraint(
                "ck_appointment_status_history_action",
                "action IN ('creation','confirmation','rejection','cancellation','completion','no_show')");
            table.HasCheckConstraint(
                "ck_appointment_status_history_creation_semantics",
                "(action = 'creation' AND sequence = 1 AND previous_status IS NULL " +
                "AND new_status = 'requested') OR " +
                "(action <> 'creation' AND sequence > 1 AND previous_status IS NOT NULL " +
                "AND previous_status <> new_status)");
        });
        builder.HasKey(history => history.Id).HasName("pk_appointment_status_history");
        SchedulingConfiguration.ConfigureId(builder, history => history.Id);
        SchedulingConfiguration.ConfigureId(
            builder,
            history => history.AppointmentId,
            "appointment_id");
        builder.Property(history => history.Sequence)
            .HasColumnName("sequence")
            .IsRequired();
        builder.Property(history => history.PreviousStatus)
            .HasColumnName("previous_status")
            .HasConversion(
                value => value.HasValue
                    ? SchedulingPersistence.StoreStatus(value.Value)
                    : null,
                value => value == null
                    ? null
                    : SchedulingPersistence.LoadStatus(value))
            .HasMaxLength(16);
        builder.Property(history => history.NewStatus)
            .HasColumnName("new_status")
            .HasConversion(
                value => SchedulingPersistence.StoreStatus(value),
                value => SchedulingPersistence.LoadStatus(value))
            .HasMaxLength(16)
            .IsRequired();
        SchedulingConfiguration.ConfigureId(
            builder,
            history => history.ActorAccountId,
            "actor_account_id");
        builder.Property(history => history.ActorType)
            .HasColumnName("actor_type")
            .HasConversion(
                value => SchedulingPersistence.StoreActorType(value),
                value => SchedulingPersistence.LoadActorType(value))
            .HasMaxLength(24)
            .IsRequired();
        builder.Property(history => history.Action)
            .HasColumnName("action")
            .HasConversion(
                value => SchedulingPersistence.StoreAction(value),
                value => SchedulingPersistence.LoadAction(value))
            .HasMaxLength(16)
            .IsRequired();
        SchedulingConfiguration.ConfigureUtc(
            builder,
            history => history.OccurredAt,
            "occurred_at");

        builder.HasIndex(history => new { history.AppointmentId, history.Sequence })
            .IsUnique()
            .HasDatabaseName("ux_appointment_status_history_appointment_sequence");
        builder.HasIndex(history => history.ActorAccountId)
            .HasDatabaseName("ix_appointment_status_history_actor_account_id");
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(history => history.ActorAccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_appointment_status_history_accounts_actor_account_id");

        SchedulingConfiguration.MakeAppendOnly(builder);
    }
}

internal sealed class AppointmentRescheduleHistoryConfiguration
    : IEntityTypeConfiguration<AppointmentRescheduleHistory>
{
    public void Configure(EntityTypeBuilder<AppointmentRescheduleHistory> builder)
    {
        builder.ToTable("appointment_reschedule_history", SchedulingConfiguration.Schema, table =>
            table.HasCheckConstraint(
                "ck_appointment_reschedule_history_distinct_slots",
                "previous_slot_id <> new_slot_id"));
        builder.HasKey(history => history.Id).HasName("pk_appointment_reschedule_history");
        SchedulingConfiguration.ConfigureId(builder, history => history.Id);
        SchedulingConfiguration.ConfigureId(
            builder,
            history => history.AppointmentId,
            "appointment_id");
        SchedulingConfiguration.ConfigureId(
            builder,
            history => history.PreviousSlotId,
            "previous_slot_id");
        SchedulingConfiguration.ConfigureId(
            builder,
            history => history.NewSlotId,
            "new_slot_id");
        SchedulingConfiguration.ConfigureId(
            builder,
            history => history.ActorAccountId,
            "actor_account_id");
        SchedulingConfiguration.ConfigureUtc(
            builder,
            history => history.OccurredAt,
            "occurred_at");

        builder.HasIndex(history => new
        {
            history.AppointmentId,
            history.OccurredAt,
            history.Id
        }).HasDatabaseName("ix_appointment_reschedule_history_appointment_occurred_id");
        builder.HasIndex(history => history.PreviousSlotId)
            .HasDatabaseName("ix_appointment_reschedule_history_previous_slot_id");
        builder.HasIndex(history => history.NewSlotId)
            .HasDatabaseName("ix_appointment_reschedule_history_new_slot_id");
        builder.HasIndex(history => history.ActorAccountId)
            .HasDatabaseName("ix_appointment_reschedule_history_actor_account_id");

        builder.HasOne<Appointment>()
            .WithMany()
            .HasForeignKey(history => history.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_appointment_reschedule_history_appointments_appointment_id");
        builder.HasOne<AvailabilitySlot>()
            .WithMany()
            .HasForeignKey(history => history.PreviousSlotId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_appointment_reschedule_history_previous_slot_id");
        builder.HasOne<AvailabilitySlot>()
            .WithMany()
            .HasForeignKey(history => history.NewSlotId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_appointment_reschedule_history_new_slot_id");
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(history => history.ActorAccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_appointment_reschedule_history_accounts_actor_account_id");

        SchedulingConfiguration.MakeAppendOnly(builder);
    }
}
