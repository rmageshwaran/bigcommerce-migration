# Change Request: Multi-Channel Category Migration Support

**Document Version:** 1.0  
**Date:** 2024-07-30

---

## 1. Change Description

This document proposes a fundamental change to the migration workflow to support many-to-many channel migrations within a single migration job. The current implementation supports a one-to-one mapping, where a single source channel ID and a single destination channel ID are provided in the `MigrationRequest`.

The proposed change will deprecate the `SourceStore.ChannelId` and `DestinationStore.ChannelId` properties and introduce a new `ChannelMapping` collection in the `MigrationRequest`. This collection will contain a list of objects, each mapping a specific source channel ID to a destination channel ID.

## 2. Reason for Change

The primary driver for this change is to enhance the capabilities of the migration tool to handle more complex, real-world e-commerce scenarios. Many businesses operate across multiple channels (e.g., different regional storefronts, B2B vs. B2C sites). This change will allow them to:

-   Consolidate multiple source channels into a single destination channel.
-   Migrate multiple source channels to their corresponding destination channels in a single, orchestrated operation.
-   Greatly improve the efficiency and user experience for complex migrations, reducing the need for multiple separate migration jobs.

## 3. Impacted Components

This is a significant architectural change that will impact the data flow from the initial request down to the entity processing strategies. The following components are expected to be modified:

-   **`BigCommerce.Migration.Core`**:
    -   `MigrationRequest.cs`: The core model will be updated to include the `ChannelMapping`.
    -   `StoreConfiguration.cs`: The `ChannelId` property will be deprecated/removed.
    -   `CategoryTreeContext.cs`: Will be updated to hold a mapping of source-to-destination category tree IDs instead of single IDs.

-   **`BigCommerce.Migration.Functions`**:
    -   `MigrationHttpFunctions.cs`: The API endpoint for starting a migration will need to be updated to handle the new `ChannelMapping` in the request body.

-   **`BigCommerce.Migration.Activities`**:
    -   `ResolveCategoryTreeIdsActivity.cs`: This activity will undergo significant changes. It will need to iterate through all channel mappings, resolve the corresponding category tree IDs for each, and build a map of source tree IDs to destination tree IDs.
    -   `V3HierarchicalStrategy.cs`: The category discovery strategy will need to be updated to discover categories from multiple source category trees.
    -   `ProcessEntityChunkActivity.cs` and its underlying services (`EntityTransformService`, `EntityCreateService`): These will need to be updated to use the new category tree ID mapping to ensure categories are placed correctly in the destination store.

-   **`BigCommerce.Migration.Tests`**:
    -   Unit tests for all the above components will need to be created or updated to reflect the new logic.

## 4. Proposed Solution

The solution will be implemented by modifying the workflow to be driven by the `ChannelMapping` collection.

1.  The `MigrationRequest` will be updated to accept a list of channel mappings.
2.  The `ResolveCategoryTreeIdsActivity` will be enhanced to query the BigCommerce API for the category trees associated with each channel in the mapping. It will produce a `CategoryTreeContext` that contains a dictionary mapping each source category tree ID to its corresponding destination category tree ID.
3.  The `V3HierarchicalStrategy` will be modified to iterate over all the source category tree IDs from the context. It will discover and aggregate all categories from all specified trees. The discovered category data will be tagged with its source tree ID.
4.  The transformation and creation logic will use the source tree ID tag on each category to look up the correct destination tree ID from the mapping in the `CategoryTreeContext`. This will ensure that categories are created with the correct parent-child relationships within the correct destination tree.

This approach will create a robust, scalable, and flexible multi-channel migration capability while integrating smoothly with the existing orchestrated workflow.
