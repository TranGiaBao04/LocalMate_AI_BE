using LocalMateAI.Application.DTOs.Users;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

public sealed class UserService(
    IUserRepository userRepository,
    ITagRepository tagRepository) : IUserService
{
    private const int MaximumFullNameLength = 200;

    public async Task<UserProfileResponse?> GetCurrentUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);

        return user is null ? null : MapProfile(user);
    }

    public async Task<UpdateCurrentUserResult> UpdateCurrentUserAsync(
        Guid userId,
        UpdateCurrentUserRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var hasFullName = request.FullName is not null;
        var interestTagIds = request.Preferences?.InterestTagIds?.ToHashSet();
        var travelStyleTagIds = request.Preferences?.TravelStyleTagIds?.ToHashSet();

        if (!hasFullName && interestTagIds is null && travelStyleTagIds is null)
        {
            return UpdateCurrentUserResult.NoChanges();
        }

        var fullName = request.FullName?.Trim();
        var validationErrors = ValidateFullName(hasFullName, fullName);
        if (validationErrors.Count > 0)
        {
            return UpdateCurrentUserResult.ValidationFailed(validationErrors);
        }

        if (ContainsEmptyId(interestTagIds) || ContainsEmptyId(travelStyleTagIds))
        {
            return UpdateCurrentUserResult.InvalidPreference();
        }

        var user = await userRepository.GetByIdForUpdateAsync(userId, cancellationToken);
        if (user is null)
        {
            return UpdateCurrentUserResult.UserNotFound();
        }

        var requestedTagIds = new HashSet<Guid>();
        if (interestTagIds is not null)
        {
            requestedTagIds.UnionWith(interestTagIds);
        }

        if (travelStyleTagIds is not null)
        {
            requestedTagIds.UnionWith(travelStyleTagIds);
        }

        IReadOnlyList<Tag> requestedTags = [];
        if (requestedTagIds.Count > 0)
        {
            requestedTags = await tagRepository.GetByIdsAsync(
                requestedTagIds,
                cancellationToken);
        }

        var tagsById = requestedTags.ToDictionary(tag => tag.Id);
        if (tagsById.Count != requestedTagIds.Count
            || !AreValidPreferences(interestTagIds, TagType.Interest, tagsById)
            || !AreValidPreferences(travelStyleTagIds, TagType.TravelStyle, tagsById))
        {
            return UpdateCurrentUserResult.InvalidPreference();
        }

        var currentInterestTagIds = GetPreferenceIds(user, TagType.Interest);
        var currentTravelStyleTagIds = GetPreferenceIds(user, TagType.TravelStyle);

        var fullNameChanged = hasFullName
            && !string.Equals(user.FullName, fullName, StringComparison.Ordinal);
        var interestsChanged = interestTagIds is not null
            && !currentInterestTagIds.SetEquals(interestTagIds);
        var travelStylesChanged = travelStyleTagIds is not null
            && !currentTravelStyleTagIds.SetEquals(travelStyleTagIds);

        if (!fullNameChanged && !interestsChanged && !travelStylesChanged)
        {
            return UpdateCurrentUserResult.Succeeded(
                MapProfile(user, currentInterestTagIds, currentTravelStyleTagIds));
        }

        if (fullNameChanged)
        {
            user.FullName = fullName!;
        }

        if (interestsChanged)
        {
            ReplacePreferenceGroup(user, TagType.Interest, interestTagIds!);
        }

        if (travelStylesChanged)
        {
            ReplacePreferenceGroup(user, TagType.TravelStyle, travelStyleTagIds!);
        }

        await userRepository.SaveProfileChangesAsync(user, cancellationToken);

        return UpdateCurrentUserResult.Succeeded(
            MapProfile(
                user,
                interestTagIds ?? currentInterestTagIds,
                travelStyleTagIds ?? currentTravelStyleTagIds));
    }

    private static Dictionary<string, string[]> ValidateFullName(
        bool hasFullName,
        string? fullName)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!hasFullName)
        {
            return errors;
        }

        if (string.IsNullOrWhiteSpace(fullName))
        {
            errors["fullName"] = ["Full name is required."];
        }
        else if (fullName.Length > MaximumFullNameLength)
        {
            errors["fullName"] =
                [$"Full name must not exceed {MaximumFullNameLength} characters."];
        }

        return errors;
    }

    private static bool ContainsEmptyId(HashSet<Guid>? tagIds) =>
        tagIds?.Contains(Guid.Empty) == true;

    private static bool AreValidPreferences(
        HashSet<Guid>? tagIds,
        TagType expectedType,
        IReadOnlyDictionary<Guid, Tag> tagsById) =>
        tagIds is null
        || tagIds.All(tagId =>
            tagsById.TryGetValue(tagId, out var tag)
            && tag.IsActive
            && tag.Type == expectedType);

    private static HashSet<Guid> GetPreferenceIds(User user, TagType type) =>
        user.PreferenceTags
            .Where(preferenceTag => preferenceTag.Tag.Type == type)
            .Select(preferenceTag => preferenceTag.TagId)
            .ToHashSet();

    private static void ReplacePreferenceGroup(
        User user,
        TagType type,
        IReadOnlySet<Guid> requestedTagIds)
    {
        var currentPreferenceTags = user.PreferenceTags
            .Where(preferenceTag =>
                preferenceTag.Tag is not null
                && preferenceTag.Tag.Type == type)
            .ToArray();

        foreach (var preferenceTag in currentPreferenceTags)
        {
            if (!requestedTagIds.Contains(preferenceTag.TagId))
            {
                user.PreferenceTags.Remove(preferenceTag);
            }
        }

        var currentTagIds = currentPreferenceTags
            .Select(preferenceTag => preferenceTag.TagId)
            .ToHashSet();

        foreach (var tagId in requestedTagIds.Except(currentTagIds))
        {
            user.PreferenceTags.Add(new UserPreferenceTag
            {
                UserId = user.Id,
                TagId = tagId
            });
        }
    }

    private static UserProfileResponse MapProfile(User user)
    {
        var interestTagIds = GetPreferenceIds(user, TagType.Interest);
        var travelStyleTagIds = GetPreferenceIds(user, TagType.TravelStyle);

        return MapProfile(user, interestTagIds, travelStyleTagIds);
    }

    private static UserProfileResponse MapProfile(
        User user,
        IEnumerable<Guid> interestTagIds,
        IEnumerable<Guid> travelStyleTagIds) =>
        new(
            user.Id,
            user.FullName,
            user.Email,
            user.Role.ToString(),
            user.CreatedAt,
            new UserPreferencesResponse(
                interestTagIds.Order().ToArray(),
                travelStyleTagIds.Order().ToArray()));
}
