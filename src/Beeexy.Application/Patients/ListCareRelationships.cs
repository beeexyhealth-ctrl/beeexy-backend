namespace Beeexy.Application.Patients;

public sealed class ListCareRelationships(
    CurrentAccountProfileResolver currentAccountResolver,
    IMyCircleReadRepository repository)
{
    public async Task<ListCareRelationshipsResult> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var current = await currentAccountResolver.ResolveAsync(cancellationToken);
        var relationships = await repository.ListRelationshipsAsync(
            current.PrimaryProfile.Id,
            cancellationToken);

        return new ListCareRelationshipsResult(
            relationships
                .OrderBy(value => value.CreatedAt)
                .ThenBy(value => value.RelationshipId.Value)
                .ToArray());
    }
}

public sealed record ListCareRelationshipsResult(
    IReadOnlyList<CareRelationshipListRecord> Relationships);
