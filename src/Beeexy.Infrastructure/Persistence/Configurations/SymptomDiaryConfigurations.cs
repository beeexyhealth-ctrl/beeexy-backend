using Beeexy.Domain.Care;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Triage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Beeexy.Infrastructure.Persistence.Configurations;

internal sealed class SymptomDiaryPackageVersionConfiguration
    : IEntityTypeConfiguration<SymptomDiaryPackageVersion>
{
    public void Configure(EntityTypeBuilder<SymptomDiaryPackageVersion> builder)
    {
        builder.ToTable("symptom_diary_package_versions", "care", table =>
        {
            table.HasCheckConstraint("ck_symptom_diary_packages_package_code", "length(btrim(package_code)) > 0");
            table.HasCheckConstraint("ck_symptom_diary_packages_package_version", "length(btrim(package_version)) > 0");
            table.HasCheckConstraint("ck_symptom_diary_packages_question_set_code", "length(btrim(question_set_code)) > 0");
            table.HasCheckConstraint("ck_symptom_diary_packages_question_set_version", "length(btrim(question_set_version)) > 0");
            table.HasCheckConstraint("ck_symptom_diary_packages_information_code", "length(btrim(symptom_information_code)) > 0");
            table.HasCheckConstraint("ck_symptom_diary_packages_information_version", "length(btrim(symptom_information_version)) > 0");
            table.HasCheckConstraint("ck_symptom_diary_packages_pathway", "length(btrim(pathway_code)) > 0");
            table.HasCheckConstraint("ck_symptom_diary_packages_content_hash", "canonical_content_hash ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint(
                "ck_symptom_diary_packages_content_source",
                "clinical_content_source IN ('LEGACY_UNSPECIFIED', 'REFERENCE_PLATFORM_DERIVED', 'PRODUCT_DEMO_DEFINED', 'MEDICAL_TEAM_PROVIDED')");
            table.HasCheckConstraint(
                "ck_symptom_diary_packages_review_status",
                "clinical_review_status IN ('REVIEWED', 'PROVISIONAL', 'NOT_APPLICABLE')");
            table.HasCheckConstraint(
                "ck_symptom_diary_packages_approval_status",
                "clinical_approval_status IN ('APPROVED', 'PENDING_FORMAL_REVIEW', 'NOT_CLINICALLY_APPROVED')");
            table.HasCheckConstraint(
                "ck_symptom_diary_packages_approval",
                "(clinical_approval_status = 'APPROVED' AND approved_at IS NOT NULL) OR (clinical_approval_status <> 'APPROVED' AND approved_at IS NULL)");
            table.HasCheckConstraint(
                "ck_symptom_diary_packages_activation",
                "activated_at IS NULL OR (activated_at >= imported_at AND (approved_at IS NULL OR activated_at >= approved_at))");
            table.HasCheckConstraint(
                "ck_symptom_diary_packages_information_heading",
                "informational_heading IS NULL OR length(btrim(informational_heading)) > 0");
            table.HasCheckConstraint(
                "ck_symptom_diary_packages_information_body",
                "informational_body IS NULL OR length(btrim(informational_body)) > 0");
        });

        builder.HasKey(value => value.Id).HasName("pk_symptom_diary_package_versions");
        MapId(builder.Property(value => value.Id), "id");
        MapCode(builder.Property(value => value.PackageCode), "package_code");
        MapVersion(builder.Property(value => value.PackageVersion), "package_version");
        MapCode(builder.Property(value => value.QuestionSetCode), "question_set_code");
        MapVersion(builder.Property(value => value.QuestionSetVersion), "question_set_version");
        MapCode(builder.Property(value => value.SymptomInformationCode), "symptom_information_code");
        MapVersion(builder.Property(value => value.SymptomInformationVersion), "symptom_information_version");
        builder.Property(value => value.Pathway)
            .HasColumnName("pathway_code")
            .HasConversion(code => code.Value, value => ClinicalPathwayCode.Create(value))
            .HasMaxLength(ClinicalPathwayCode.MaximumLength)
            .IsRequired();
        builder.Property(value => value.CanonicalContentHash)
            .HasColumnName("canonical_content_hash")
            .HasConversion(hash => hash.Value, value => SymptomDiarySha256.FromHash(value))
            .HasMaxLength(SymptomDiarySha256.Length)
            .IsRequired();
        builder.Property(value => value.ContentSource)
            .HasColumnName("clinical_content_source")
            .HasConversion(
                value => ClinicalContentStatusPersistence.SerializeSource(value),
                value => ClinicalContentStatusPersistence.DeserializeSource(value))
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(value => value.ReviewStatus)
            .HasColumnName("clinical_review_status")
            .HasConversion(
                value => ClinicalContentStatusPersistence.SerializeReviewStatus(value),
                value => ClinicalContentStatusPersistence.DeserializeReviewStatus(value))
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(value => value.ApprovalStatus)
            .HasColumnName("clinical_approval_status")
            .HasConversion(
                value => ClinicalContentStatusPersistence.SerializeApprovalStatus(value),
                value => ClinicalContentStatusPersistence.DeserializeApprovalStatus(value))
            .HasMaxLength(64)
            .IsRequired();
        builder.Ignore(value => value.ContentStatus);
        builder.Property(value => value.SourceReference)
            .HasColumnName("source_reference")
            .HasMaxLength(500);
        builder.Property(value => value.ImportedAt).HasColumnName("imported_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(value => value.ApprovedAt).HasColumnName("approved_at").HasColumnType("timestamp with time zone");
        builder.Property(value => value.ActivatedAt).HasColumnName("activated_at").HasColumnType("timestamp with time zone");
        builder.Property(value => value.InformationalHeading).HasColumnName("informational_heading").HasMaxLength(4000);
        builder.Property(value => value.InformationalBody).HasColumnName("informational_body");

        builder.HasIndex(value => new { value.PackageCode, value.PackageVersion })
            .IsUnique().HasDatabaseName("ux_symptom_diary_packages_code_version");
        builder.HasIndex(value => new { value.Pathway, value.ActivatedAt })
            .HasDatabaseName("ix_symptom_diary_packages_pathway_activation");
        builder.HasMany(value => value.Questions).WithOne()
            .HasForeignKey(value => value.PackageVersionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_symptom_diary_questions_package");
        builder.Navigation(value => value.Questions).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(value => value.WarningSigns).WithOne()
            .HasForeignKey(value => value.PackageVersionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_symptom_warning_signs_package");
        builder.Navigation(value => value.WarningSigns).UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void MapId(PropertyBuilder<EntityId> property, string column) => property
        .HasColumnName(column).HasConversion(id => id.Value, value => EntityId.From(value)).ValueGeneratedNever();

    private static void MapCode(PropertyBuilder<SymptomDiaryCode> property, string column) => property
        .HasColumnName(column).HasConversion(code => code.Value, value => SymptomDiaryCode.Create(value))
        .HasMaxLength(SymptomDiaryCode.MaximumLength).IsRequired();

    private static void MapVersion(PropertyBuilder<DefinitionVersion> property, string column) => property
        .HasColumnName(column).HasConversion(version => version.Value, value => DefinitionVersion.Create(value))
        .HasMaxLength(DefinitionVersion.MaximumLength).IsRequired();
}

internal sealed class SymptomDiaryQuestionConfiguration : IEntityTypeConfiguration<SymptomDiaryQuestion>
{
    public void Configure(EntityTypeBuilder<SymptomDiaryQuestion> builder)
    {
        builder.ToTable("symptom_diary_questions", "care", table =>
        {
            table.HasCheckConstraint("ck_symptom_diary_questions_code", "length(btrim(code)) > 0");
            table.HasCheckConstraint("ck_symptom_diary_questions_prompt", "length(btrim(prompt_text)) > 0");
            table.HasCheckConstraint("ck_symptom_diary_questions_order", "source_order > 0");
            table.HasCheckConstraint("ck_symptom_diary_questions_schema", "jsonb_typeof(answer_schema) = 'object'");
        });
        builder.HasKey(value => value.Id).HasName("pk_symptom_diary_questions");
        builder.HasAlternateKey(value => new { value.Id, value.PackageVersionId })
            .HasName("ak_symptom_diary_questions_id_package");
        MapId(builder.Property(value => value.Id), "id");
        MapId(builder.Property(value => value.PackageVersionId), "package_version_id");
        builder.Property(value => value.Code).HasColumnName("code")
            .HasConversion(code => code.Value, value => SymptomDiaryCode.Create(value))
            .HasMaxLength(SymptomDiaryCode.MaximumLength).IsRequired();
        builder.Property(value => value.PromptText).HasColumnName("prompt_text").HasMaxLength(4000).IsRequired();
        builder.Property(value => value.SourceOrder).HasColumnName("source_order").IsRequired();
        builder.Property(value => value.AnswerSchemaJson).HasColumnName("answer_schema").HasColumnType("jsonb").IsRequired();
        builder.Property(value => value.IsRequired).HasColumnName("is_required").IsRequired();
        builder.HasIndex(value => new { value.PackageVersionId, value.Code }).IsUnique()
            .HasDatabaseName("ux_symptom_diary_questions_package_code");
        builder.HasIndex(value => new { value.PackageVersionId, value.SourceOrder }).IsUnique()
            .HasDatabaseName("ux_symptom_diary_questions_package_order");
        builder.HasMany(value => value.Options).WithOne()
            .HasForeignKey(value => value.QuestionId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_symptom_diary_question_options_question");
        builder.Navigation(value => value.Options).UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void MapId(PropertyBuilder<EntityId> property, string column) => property
        .HasColumnName(column).HasConversion(id => id.Value, value => EntityId.From(value)).ValueGeneratedNever();
}

internal sealed class SymptomDiaryQuestionOptionConfiguration : IEntityTypeConfiguration<SymptomDiaryQuestionOption>
{
    public void Configure(EntityTypeBuilder<SymptomDiaryQuestionOption> builder)
    {
        builder.ToTable("symptom_diary_question_options", "care", table =>
        {
            table.HasCheckConstraint("ck_symptom_diary_options_code", "length(btrim(code)) > 0");
            table.HasCheckConstraint("ck_symptom_diary_options_value", "length(btrim(value)) > 0");
            table.HasCheckConstraint("ck_symptom_diary_options_display", "length(btrim(display_text)) > 0");
            table.HasCheckConstraint("ck_symptom_diary_options_order", "source_order > 0");
        });
        builder.HasKey(value => value.Id).HasName("pk_symptom_diary_question_options");
        MapId(builder.Property(value => value.Id), "id");
        MapId(builder.Property(value => value.QuestionId), "question_id");
        builder.Property(value => value.Code).HasColumnName("code")
            .HasConversion(code => code.Value, value => SymptomDiaryCode.Create(value))
            .HasMaxLength(SymptomDiaryCode.MaximumLength).IsRequired();
        builder.Property(value => value.Value).HasColumnName("value").HasMaxLength(4000).IsRequired();
        builder.Property(value => value.DisplayText).HasColumnName("display_text").HasMaxLength(4000).IsRequired();
        builder.Property(value => value.SourceOrder).HasColumnName("source_order").IsRequired();
        builder.HasIndex(value => new { value.QuestionId, value.Code }).IsUnique()
            .HasDatabaseName("ux_symptom_diary_options_question_code");
        builder.HasIndex(value => new { value.QuestionId, value.SourceOrder }).IsUnique()
            .HasDatabaseName("ux_symptom_diary_options_question_order");
    }

    private static void MapId(PropertyBuilder<EntityId> property, string column) => property
        .HasColumnName(column).HasConversion(id => id.Value, value => EntityId.From(value)).ValueGeneratedNever();
}

internal sealed class SymptomWarningSignConfiguration : IEntityTypeConfiguration<SymptomWarningSign>
{
    public void Configure(EntityTypeBuilder<SymptomWarningSign> builder)
    {
        builder.ToTable("symptom_warning_signs", "care", table =>
        {
            table.HasCheckConstraint("ck_symptom_warning_signs_code", "length(btrim(code)) > 0");
            table.HasCheckConstraint("ck_symptom_warning_signs_display", "length(btrim(display_text)) > 0");
            table.HasCheckConstraint("ck_symptom_warning_signs_order", "source_order > 0");
        });
        builder.HasKey(value => value.Id).HasName("pk_symptom_warning_signs");
        MapId(builder.Property(value => value.Id), "id");
        MapId(builder.Property(value => value.PackageVersionId), "package_version_id");
        builder.Property(value => value.Code).HasColumnName("code")
            .HasConversion(code => code.Value, value => SymptomDiaryCode.Create(value))
            .HasMaxLength(SymptomDiaryCode.MaximumLength).IsRequired();
        builder.Property(value => value.DisplayText).HasColumnName("display_text").HasMaxLength(4000).IsRequired();
        builder.Property(value => value.SourceOrder).HasColumnName("source_order").IsRequired();
        builder.HasIndex(value => new { value.PackageVersionId, value.Code }).IsUnique()
            .HasDatabaseName("ux_symptom_warning_signs_package_code");
        builder.HasIndex(value => new { value.PackageVersionId, value.SourceOrder }).IsUnique()
            .HasDatabaseName("ux_symptom_warning_signs_package_order");
    }

    private static void MapId(PropertyBuilder<EntityId> property, string column) => property
        .HasColumnName(column).HasConversion(id => id.Value, value => EntityId.From(value)).ValueGeneratedNever();
}

internal sealed class SymptomCheckInConfiguration : IEntityTypeConfiguration<SymptomCheckIn>
{
    public void Configure(EntityTypeBuilder<SymptomCheckIn> builder)
    {
        builder.ToTable("symptom_check_ins", "care", table =>
            table.HasCheckConstraint("ck_symptom_check_ins_request_hash", "canonical_request_hash ~ '^[0-9a-f]{64}$'"));
        builder.HasKey(value => value.Id).HasName("pk_symptom_check_ins");
        builder.HasAlternateKey(value => new { value.Id, value.PackageVersionId })
            .HasName("ak_symptom_check_ins_id_package");
        MapId(builder.Property(value => value.Id), "id");
        MapId(builder.Property(value => value.EpisodeId), "episode_id");
        MapId(builder.Property(value => value.PackageVersionId), "package_version_id");
        MapId(builder.Property(value => value.SubmittingAccountId), "submitting_account_id");
        builder.Property(value => value.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
        MapId(builder.Property(value => value.IdempotencyKey), "idempotency_key");
        builder.Property(value => value.CanonicalRequestHash).HasColumnName("canonical_request_hash")
            .HasConversion(hash => hash.Value, value => SymptomDiarySha256.FromHash(value))
            .HasMaxLength(SymptomDiarySha256.Length).IsRequired();
        builder.HasIndex(value => new { value.EpisodeId, value.IdempotencyKey }).IsUnique()
            .HasDatabaseName("ux_symptom_check_ins_episode_idempotency");
        builder.HasIndex(value => new { value.EpisodeId, value.CreatedAt, value.Id })
            .HasDatabaseName("ix_symptom_check_ins_episode_created_id");
        builder.HasIndex(value => value.PackageVersionId)
            .HasDatabaseName("ix_symptom_check_ins_package");
        builder.HasIndex(value => value.SubmittingAccountId)
            .HasDatabaseName("ix_symptom_check_ins_submitting_account");
        builder.HasOne<PreTriageEpisode>().WithMany().HasForeignKey(value => value.EpisodeId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_symptom_check_ins_episode");
        builder.HasOne<SymptomDiaryPackageVersion>().WithMany().HasForeignKey(value => value.PackageVersionId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_symptom_check_ins_package");
        builder.HasOne<Account>().WithMany().HasForeignKey(value => value.SubmittingAccountId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_symptom_check_ins_submitting_account");
        builder.HasMany(value => value.Answers).WithOne()
            .HasForeignKey(value => new { value.CheckInId, value.PackageVersionId })
            .HasPrincipalKey(value => new { value.Id, value.PackageVersionId })
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_symptom_check_in_answers_check_in_package");
        builder.Navigation(value => value.Answers).UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void MapId(PropertyBuilder<EntityId> property, string column) => property
        .HasColumnName(column).HasConversion(id => id.Value, value => EntityId.From(value)).ValueGeneratedNever();
}

internal sealed class SymptomCheckInAnswerConfiguration : IEntityTypeConfiguration<SymptomCheckInAnswer>
{
    public void Configure(EntityTypeBuilder<SymptomCheckInAnswer> builder)
    {
        builder.ToTable("symptom_check_in_answers", "care", table =>
        {
            table.HasCheckConstraint("ck_symptom_check_in_answers_order", "source_order > 0");
            table.HasCheckConstraint("ck_symptom_check_in_answers_value", "submitted_value IS NOT NULL");
        });
        builder.HasKey(value => value.Id).HasName("pk_symptom_check_in_answers");
        MapId(builder.Property(value => value.Id), "id");
        MapId(builder.Property(value => value.CheckInId), "check_in_id");
        MapId(builder.Property(value => value.PackageVersionId), "package_version_id");
        MapId(builder.Property(value => value.QuestionId), "question_id");
        builder.Property(value => value.SubmittedValueJson).HasColumnName("submitted_value").HasColumnType("jsonb").IsRequired();
        builder.Property(value => value.SourceOrder).HasColumnName("source_order").IsRequired();
        builder.Property(value => value.RecordedAt).HasColumnName("recorded_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(value => new { value.CheckInId, value.PackageVersionId })
            .HasDatabaseName("ix_symptom_check_in_answers_check_in_package");
        builder.HasIndex(value => new { value.QuestionId, value.PackageVersionId })
            .HasDatabaseName("ix_symptom_check_in_answers_question_package");
        builder.HasIndex(value => new { value.CheckInId, value.QuestionId }).IsUnique()
            .HasDatabaseName("ux_symptom_check_in_answers_check_in_question");
        builder.HasOne<SymptomDiaryQuestion>().WithMany()
            .HasForeignKey(value => new { value.QuestionId, value.PackageVersionId })
            .HasPrincipalKey(value => new { value.Id, value.PackageVersionId })
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_symptom_check_in_answers_question_package");
    }

    private static void MapId(PropertyBuilder<EntityId> property, string column) => property
        .HasColumnName(column).HasConversion(id => id.Value, value => EntityId.From(value)).ValueGeneratedNever();
}
