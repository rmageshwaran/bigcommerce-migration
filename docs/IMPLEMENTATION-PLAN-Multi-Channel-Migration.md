# Implementation Plan: Multi-Channel Category Migration

**Document Version:** 1.1  
**Date:** 2024-07-30

---

## 1. Objective

To refactor the BigCommerce Migration application to support the migration of categories from multiple source channels to multiple destination channels, as defined by the existing `ChannelMapping` in the `MigrationRequest`.

## 2. Task Breakdown

The implementation will be broken down into the following tasks. Each task should be completed and tested before moving to the next.

### Task 1: Update Core Models

**Description:** Modify the core data models to support multi-channel migrations.

**Sub-tasks:**
1.1. In `BigCommerce.Migration.Core/Models/StoreConfiguration.cs`:
    - Delete the `ChannelId` property.
1.2. In `BigCommerce.Migration.Core/Models/CategoryTreeContext.cs`:
    - Replace `SourceCategoryTreeId` and `DestinationCategoryTreeId` with `public Dictionary<string, string> CategoryTreeIdMapping { get; set; }`. This will map source tree IDs to destination tree IDs.
    - Also, update any other relevant properties.

### Task 2: Update `ResolveCategoryTreeIdsActivity`

**Description:** Refactor the activity to resolve category tree IDs for all channel mappings efficiently.

**Sub-tasks:**
2.1. In `BigCommerce.Migration.Activities/Activities/ResolveCategoryTreeIdsActivity.cs`:
    - Modify the `ResolveCategoryTreeIdsAsync` method to use the `migrationRequest.ChannelMapping`.
    - Aggregate all unique source and destination channel IDs from the mapping.
    - Make a single API call to the source store to fetch all relevant category trees using the `channel_id:in=` query parameter.
    - Make a single API call to the destination store to fetch all relevant category trees.
    - In memory, map the retrieved trees back to their respective channels.
    - Populate the `CategoryTreeContext.CategoryTreeIdMapping` dictionary by pairing the source and destination tree IDs based on the original `ChannelMapping`.
    - Ensure robust error handling for cases where a tree ID cannot be resolved for a specific channel.

### Task 3: Update `V3HierarchicalStrategy` for Discovery

**Description:** Enhance the category discovery strategy to fetch categories from multiple source trees.

**Sub-tasks:**
3.1. In `BigCommerce.Migration.Activities/Strategies/V3HierarchicalStrategy.cs`:
    - In `DiscoverEntitiesAsync`, iterate through the `request.CategoryTreeContext.CategoryTreeIdMapping.Keys` (the source tree IDs).
    - For each `SourceCategoryTreeId`, perform the pagination and fetching of categories as is currently done.
    - Aggregate the discovered categories from all trees into a single list.
    - **Crucially**, before adding to the aggregated list, tag each discovered category with its `SourceCategoryTreeId`. This can be done by adding a new key-value pair to the category's dictionary, e.g., `"_sourceTreeId": "treeId"`.
    - The final result should be a single list of all categories from all source trees, with each category tagged with its origin tree.

### Task 4: Update Transformation and Creation Logic

**Description:** Modify the entity processing pipeline to use the tree ID mapping for correct placement of categories.

**Sub-tasks:**
4.1. In `BigCommerce.Migration.Activities/Services/EntityTransformService.cs` (or relevant transform strategy):
    - When transforming a category, extract the `_sourceTreeId` tag.
    - Use this tag to look up the corresponding `destinationTreeId` from the `CategoryTreeContext.CategoryTreeIdMapping`.
    - Ensure the transformed category object includes this `destinationTreeId` so the creation service knows where to place it. This may involve modifying the `parent_id` logic to ensure it's relative to the new tree.
4.2. In `BigCommerce.Migration.Activities/Services/EntityCreateService.cs` (or relevant creation strategy):
    - When creating a category, use the `destinationTreeId` from the transformed entity data to correctly associate the category with its parent and the destination tree.

### Task 5: Update Unit Tests

**Description:** Create and update unit tests to ensure the new multi-channel functionality is working correctly and doesn't introduce regressions.

**Sub-tasks:**
5.1. Write new unit tests for `ResolveCategoryTreeIdsActivity` to verify its efficient handling of the `ChannelMapping` and creation of the `CategoryTreeIdMapping`.
5.2. Update unit tests for `V3HierarchicalStrategy` to test the discovery of categories from multiple trees.
5.3. Update unit tests for the transformation and creation services to ensure they correctly use the tree ID mapping.
5.4. Ensure all existing tests continue to pass.

## 3. Timeline

This is a multi-step process that should be implemented incrementally. A rough estimate for completion is 3-5 development sessions, assuming each major task and its testing are completed in a single session.

## 4. Risks and Mitigation

-   **Breaking Changes**: The removal of `ChannelId` from `StoreConfiguration` is a breaking change for the API.
    -   **Mitigation**: The API version should be incremented, and documentation should be updated to reflect the new request structure, emphasizing the use of `ChannelMapping`.
-   **Performance**: Fetching categories from multiple trees could increase the duration of the discovery phase.
    -   **Mitigation**: The discovery process already uses efficient pagination. The impact is expected to be linear with the number of channels and can be monitored. The tree resolution has been optimized to use bulk API calls.
-   **Error Handling**: A failure to resolve a tree for one channel should not necessarily fail the entire migration.
    -   **Mitigation**: The `ResolveCategoryTreeIdsActivity` will be designed to log errors for individual channels and, depending on the desired behavior, either continue with the successful mappings or fail the entire migration. This will be a configurable option.
