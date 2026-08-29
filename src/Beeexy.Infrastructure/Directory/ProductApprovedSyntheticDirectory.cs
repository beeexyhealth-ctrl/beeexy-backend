using Beeexy.Application.Directory;
using Beeexy.Domain.Common;
using Beeexy.Domain.Directory;

namespace Beeexy.Infrastructure.DirectoryServices;

public static class ProductApprovedSyntheticDirectory
{
    public const string PackageCode = "beeexy-synthetic-demo-directory";
    public const string Version = "2026.08.29-demo.1";
    public const string ExpectedContentHash =
        "82da23f40c8f92f135fb2413ccfc8e794f8bb7eb56e3a77bfe19a0d1d850601a";

    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 29, 0, 0, 0, TimeSpan.Zero);

    public static DirectoryImportPackage Create()
    {
        var clinics = CreateClinics();
        var locations = CreateLocations();
        var doctors = CreateDoctors();
        var specialties = CreateSpecialties();
        var languages = CreateLanguages();
        var insurancePlans = CreateInsurancePlans();

        var package = DirectoryImportPackage.Create(
            DirectoryCode.Create(PackageCode),
            DirectoryCode.Create(Version),
            clinics,
            locations,
            doctors,
            CreateAffiliations(),
            CreateCredentials(),
            specialties,
            CreateDoctorSpecialties(),
            languages,
            CreateDoctorLanguages(),
            insurancePlans,
            CreateInsuranceParticipations());
        if (!string.Equals(package.ContentHash, ExpectedContentHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The approved synthetic directory content changed. Review the dataset and assign " +
                "a new version and expected content hash before importing it.");
        }

        return package;
    }

    private static Clinic[] CreateClinics() =>
    [
        Clinic.Create(
            DirectoryCode.Create("demo-clinic-aurora"),
            DirectoryName.Create("Synthetic Demo Clinic Aurora"),
            true,
            CreatedAt,
            Id("71020000-0000-4000-8000-000000000001")),
        Clinic.Create(
            DirectoryCode.Create("demo-clinic-mosaic"),
            DirectoryName.Create("Synthetic Demo Clinic Mosaic"),
            true,
            CreatedAt,
            Id("71020000-0000-4000-8000-000000000002")),
        Clinic.Create(
            DirectoryCode.Create("demo-clinic-archive"),
            DirectoryName.Create("Synthetic Demo Clinic Archive (Unpublished)"),
            false,
            CreatedAt,
            Id("71020000-0000-4000-8000-000000000003"))
    ];

    private static ClinicLocation[] CreateLocations() =>
    [
        Location(
            "71020000-0000-4100-8000-000000000011",
            "71020000-0000-4000-8000-000000000001",
            "Synthetic Aurora Central Location",
            "Demo Central",
            true),
        Location(
            "71020000-0000-4100-8000-000000000012",
            "71020000-0000-4000-8000-000000000001",
            "Synthetic Aurora Hidden Location",
            "Demo North",
            false),
        Location(
            "71020000-0000-4100-8000-000000000013",
            "71020000-0000-4000-8000-000000000002",
            "Synthetic Mosaic Harbor Location",
            "Demo Harbor",
            true),
        Location(
            "71020000-0000-4100-8000-000000000014",
            "71020000-0000-4000-8000-000000000003",
            "Synthetic Archive Location",
            "Demo Archive",
            true)
    ];

    private static Doctor[] CreateDoctors() =>
    [
        Doctor("21", "demo-doctor-amber", "Synthetic Demo Doctor Amber", true),
        Doctor("22", "demo-doctor-blue", "Synthetic Demo Doctor Blue", true),
        Doctor("23", "demo-doctor-coral", "Synthetic Demo Doctor Coral", true),
        Doctor("24", "demo-doctor-dusk", "Synthetic Demo Doctor Dusk (Unpublished)", false),
        Doctor("25", "demo-doctor-ember", "Synthetic Demo Doctor Ember", true)
    ];

    private static DoctorAffiliation[] CreateAffiliations() =>
    [
        Affiliation("31", "21", "01", "11", true),
        Affiliation("32", "22", "01", "12", true),
        Affiliation("33", "22", "02", "13", true),
        Affiliation("34", "23", "03", "14", true),
        Affiliation("35", "24", "01", "11", true),
        Affiliation("36", "25", "01", "11", false),
        Affiliation("37", "25", "02", null, true)
    ];

    private static DoctorCredential[] CreateCredentials() =>
    [
        Credential("41", "21", "Synthetic Demo Dataset Credential Amber", DoctorCredentialStatus.Verified),
        Credential("42", "21", "Synthetic Demo Dataset Claim Amber", DoctorCredentialStatus.Submitted),
        Credential("43", "22", "Synthetic Demo Dataset Claim Blue", DoctorCredentialStatus.PendingVerification),
        Credential("44", "23", "Synthetic Demo Dataset Claim Coral", DoctorCredentialStatus.Rejected),
        Credential("45", "24", "Synthetic Demo Dataset Credential Dusk", DoctorCredentialStatus.Verified),
        Credential("46", "25", "Synthetic Demo Dataset Credential Ember", DoctorCredentialStatus.Verified)
    ];

    private static Specialty[] CreateSpecialties() =>
    [
        Specialty("51", "demo-specialty-general", "Synthetic General Care"),
        Specialty("52", "demo-specialty-child", "Synthetic Child Care"),
        Specialty("53", "demo-specialty-neuro", "Synthetic Neurological Care")
    ];

    private static DoctorSpecialty[] CreateDoctorSpecialties() =>
    [
        DoctorSpecialty("61", "21", "51"),
        DoctorSpecialty("62", "22", "51"),
        DoctorSpecialty("63", "23", "52"),
        DoctorSpecialty("64", "24", "53"),
        DoctorSpecialty("65", "25", "52")
    ];

    private static Language[] CreateLanguages() =>
    [
        Language("71", "demo-language-es", "Synthetic Spanish Capability"),
        Language("72", "demo-language-en", "Synthetic English Capability"),
        Language("73", "demo-language-pt", "Synthetic Portuguese Capability")
    ];

    private static DoctorLanguage[] CreateDoctorLanguages() =>
    [
        DoctorLanguage("81", "21", "71"),
        DoctorLanguage("82", "21", "72"),
        DoctorLanguage("83", "22", "71"),
        DoctorLanguage("84", "23", "73"),
        DoctorLanguage("85", "24", "72"),
        DoctorLanguage("86", "25", "71")
    ];

    private static InsurancePlan[] CreateInsurancePlans() =>
    [
        InsurancePlan("91", "demo-plan-amber", "Synthetic Stored Plan Amber"),
        InsurancePlan("92", "demo-plan-blue", "Synthetic Stored Plan Blue"),
        InsurancePlan("93", "demo-plan-coral", "Synthetic Stored Plan Coral")
    ];

    private static DoctorInsuranceParticipation[] CreateInsuranceParticipations() =>
    [
        Participation("a1", "21", "91"),
        Participation("a2", "21", "92"),
        Participation("a3", "22", "92"),
        Participation("a4", "23", "93"),
        Participation("a5", "24", "91"),
        Participation("a6", "25", "93")
    ];

    private static ClinicLocation Location(
        string id,
        string clinicId,
        string name,
        string locality,
        bool published) =>
        ClinicLocation.Create(
            Id(clinicId),
            DirectoryName.Create(name),
            locality,
            "Synthetic Demo Region",
            "Synthetic Demo Country",
            IanaTimeZone.Create("America/Lima"),
            published,
            CreatedAt,
            Id(id));

    private static Doctor Doctor(string id, string code, string name, bool published) =>
        Beeexy.Domain.Directory.Doctor.Create(
            DirectoryCode.Create(code),
            DirectoryName.Create(name),
            published,
            CreatedAt,
            CategoryId("2", id));

    private static DoctorAffiliation Affiliation(
        string id,
        string doctorId,
        string clinicId,
        string? locationId,
        bool published) =>
        DoctorAffiliation.Create(
            CategoryId("2", doctorId),
            CategoryId("0", clinicId),
            locationId is null ? null : CategoryId("1", locationId),
            published,
            CreatedAt,
            CategoryId("3", id));

    private static DoctorCredential Credential(
        string id,
        string doctorId,
        string name,
        DoctorCredentialStatus status) =>
        DoctorCredential.Create(
            CategoryId("2", doctorId),
            DirectoryName.Create(name),
            status,
            CreatedAt,
            CategoryId("4", id));

    private static Specialty Specialty(string id, string code, string name) =>
        Beeexy.Domain.Directory.Specialty.Create(
            DirectoryCode.Create(code),
            DirectoryName.Create(name),
            CreatedAt,
            CategoryId("5", id));

    private static DoctorSpecialty DoctorSpecialty(string id, string doctorId, string specialtyId) =>
        Beeexy.Domain.Directory.DoctorSpecialty.Create(
            CategoryId("2", doctorId),
            CategoryId("5", specialtyId),
            CreatedAt,
            CategoryId("6", id));

    private static Language Language(string id, string code, string name) =>
        Beeexy.Domain.Directory.Language.Create(
            DirectoryCode.Create(code),
            DirectoryName.Create(name),
            CreatedAt,
            CategoryId("7", id));

    private static DoctorLanguage DoctorLanguage(string id, string doctorId, string languageId) =>
        Beeexy.Domain.Directory.DoctorLanguage.Create(
            CategoryId("2", doctorId),
            CategoryId("7", languageId),
            CreatedAt,
            CategoryId("8", id));

    private static InsurancePlan InsurancePlan(string id, string code, string name) =>
        Beeexy.Domain.Directory.InsurancePlan.Create(
            DirectoryCode.Create(code),
            DirectoryName.Create(name),
            CreatedAt,
            CategoryId("9", id));

    private static DoctorInsuranceParticipation Participation(
        string id,
        string doctorId,
        string insuranceId) =>
        DoctorInsuranceParticipation.Create(
            CategoryId("2", doctorId),
            CategoryId("9", insuranceId),
            CreatedAt,
            CategoryId("a", id));

    private static EntityId CategoryId(string category, string suffix) =>
        Id($"71020000-0000-4{category}00-8000-0000000000{suffix}");

    private static EntityId Id(string value) => EntityId.From(Guid.Parse(value));
}
