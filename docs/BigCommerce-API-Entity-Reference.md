# BigCommerce API Entity Reference Guide

## 📋 Document Overview

This document provides a comprehensive reference for all BigCommerce API entities and their capabilities, based on official documentation analysis. This serves as the technical foundation for our BigCommerce Migration System architecture and implementation.

**Sources:**
- [REST Catalog API Documentation](https://developer.bigcommerce.com/docs/rest-catalog)
- [REST Management API Documentation](https://developer.bigcommerce.com/docs/rest-management)
- [BigCommerce Developer Portal](https://developer.bigcommerce.com/)

**Last Updated:** January 2025  
**API Versions:** V3 (Current), V2 (Legacy Support)

---

## 🎯 API Architecture Overview

### **Primary API Categories**

1. **REST Catalog API** - Product catalog management
2. **REST Management API** - Store operations and configuration
3. **GraphQL Storefront API** - Frontend/headless commerce
4. **Specialized APIs** - Checkout, Customer Login, Payments

### **Rate Limiting**
- **12 requests/second** per store
- **30-second sliding window** for rate limit calculation
- **Distributed rate limiting** required for high-volume operations

---

## 📊 REST Catalog API Entities

### **1. Products**
**Endpoint Base:** `/v3/catalog/products`

**Core Operations:**
- `GET /products` - Get All Products
- `POST /products` - Create a Product  
- `PUT /products/{id}` - Update a Product
- `DELETE /products/{id}` - Delete a Product
- `PUT /products` - Update Products (Batch)

**Advanced Components:**
- **Bulk Pricing Rules** - Volume-based pricing strategies
- **Category Assignments** - Product-category relationships
- **Channel Assignments** - Multi-channel product distribution
- **Complex Rules** - Advanced product logic and conditional pricing
- **Custom Fields** - Additional product attributes
- **Images** - Multiple product images with management
- **Reviews** - Customer product review system
- **Videos** - Product video content management
- **Metafields** - Extended product metadata

**Migration Considerations:**
- Supports batch operations for bulk migration efficiency
- Complex products with 500+ variants supported
- Images and videos can be processed in parallel
- Custom fields allow for additional source system data

### **2. Categories**
**Endpoint Base:** `/v3/catalog/categories`

**Modern Approach - Category Trees:**
- `GET /category-trees` - Get all category trees
- `PUT /category-trees` - Upsert category trees
- `DELETE /category-trees` - Delete category trees

**Legacy Support:**
- Traditional category API marked as **deprecated**
- Still functional but not recommended for new implementations

**Features:**
- **Hierarchical Structure** - Unlimited depth parent-child relationships
- **Image Support** - Category image management
- **Sort Order** - Product ordering within categories
- **Metafields** - Extended category metadata
- **Batch Operations** - Efficient bulk category management

**Migration Priority:** High - Categories must exist before product assignments

### **3. Brands**
**Endpoint Base:** `/v3/catalog/brands`

**Operations:**
- `GET /brands` - Get All Brands
- `POST /brands` - Create a Brand
- `PUT /brands/{id}` - Update a Brand
- `DELETE /brands/{id}` - Delete a Brand

**Features:**
- **Simple CRUD** - Straightforward brand management
- **Image Support** - Brand logo and image management
- **Metafields** - Extended brand metadata
- **Batch Operations** - Bulk brand metafield management

**Migration Priority:** High - Brands must exist before product assignments

### **4. Product Variants**
**Endpoint Base:** `/v3/catalog/products/{product_id}/variants`

**Operations:**
- `GET /variants` - Get all product variants
- `POST /variants` - Create a product variant
- `PUT /variants/{id}` - Update a product variant
- `DELETE /variants/{id}` - Delete a product variant
- `PUT /variants` - Update Variants (Batch)

**Features:**
- **Individual Management** - Single variant operations
- **Batch Operations** - Efficient bulk variant updates
- **Images** - Variant-specific image management
- **Metafields** - Variant-level metadata
- **Option Value Mapping** - Links to product variant options

**Migration Considerations:**
- Process after parent products
- Batch operations critical for products with many variants
- Images can be processed in parallel sub-orchestrations

### **5. Product Modifiers**
**Endpoint Base:** `/v3/catalog/products/{product_id}/modifiers`

**Operations:**
- `GET /modifiers` - Get all product modifiers
- `POST /modifiers` - Create a product modifier
- `PUT /modifiers/{id}` - Update a product modifier
- `DELETE /modifiers/{id}` - Delete a product modifier

**Sub-Resources:**
- **Values** - Modifier option values
- **Images** - Modifier-specific images

**Migration Use Cases:**
- Product customizations and add-ons
- Additional pricing components
- Custom product configurations

### **6. Product Variant Options**
**Endpoint Base:** `/v3/catalog/products/{product_id}/options`

**Operations:**
- `GET /options` - Get All Product Variant Options
- `POST /options` - Create a Product Variant Option
- `PUT /options/{id}` - Update a Product Variant Option
- `DELETE /options/{id}` - Delete a Product Variant Option

**Sub-Resources:**
- **Values** - Individual option values (Size: Small, Medium, Large)

**Features:**
- **Reusable** - Options can be applied to multiple products
- **Variant Generation** - Options create product variants
- **Flexible Types** - Text, number, date, file, etc.

---

## 🏢 REST Management API Entities

### **1. Customers**
**Current:** `/v3/customers` | **Legacy:** `/v2/customers`

**V3 Operations (Recommended):**
- `GET /customers` - Get All Customers
- `POST /customers` - Create a Customer
- `PUT /customers/{id}` - Update a Customer
- `DELETE /customers/{id}` - Delete a Customer

**Advanced Features:**
- **Addresses** - Customer address management
- **Attributes** - Custom customer attributes
- **Form Field Values** - Additional customer data
- **Metafields** - Extended customer metadata
- **Stored Instruments** - Payment method storage

**Migration Priority:** Medium - Process after customer groups

### **2. Customer Groups**
**Endpoint Base:** `/v2/customer_groups`

**Operations:**
- `GET /customer_groups` - Get All Customer Groups
- `POST /customer_groups` - Create a Customer Group
- `PUT /customer_groups/{id}` - Update a Customer Group
- `DELETE /customer_groups/{id}` - Delete a Customer Group

**Features:**
- **Customer Segmentation** - Group customers for pricing/access
- **Pricing Rules** - Group-based pricing strategies
- **Access Control** - Category and product access permissions

**Migration Priority:** Very High - Must process before customers and products

### **3. Orders**
**Endpoint Base:** `/v2/orders` and `/v3/orders`

**Comprehensive Features:**
- **Order Management** - Full order lifecycle
- **Line Items** - Individual order products
- **Addresses** - Billing and shipping address management
- **Status Management** - Order fulfillment workflow
- **Metafields** - Extended order metadata
- **Coupons** - Applied discount codes
- **Shipments** - Shipping and tracking information

**Migration Considerations:**
- Large volume entity - requires careful batching
- Complex relationships with products, customers, addresses
- Historical data preservation critical

### **4. Carts & Checkouts**

**Carts:** `/v3/carts`
- **Cart Management** - Shopping cart operations
- **Line Items** - Cart product management
- **Settings** - Cart configuration
- **Metafields** - Extended cart metadata

**Checkouts:** `/v3/checkouts`
- **Checkout Process** - Complete checkout workflow
- **Billing/Shipping** - Address management
- **Customer Messages** - Checkout communications

### **5. Channels**
**Endpoint Base:** `/v3/channels`

**Multi-Storefront Capabilities:**
- **Channel Management** - Multiple sales channels
- **Listings** - Product channel assignments
- **Sites** - Channel-specific site configuration
- **Menus** - Channel-specific navigation
- **Currency Assignments** - Multi-currency support

**Migration Use Cases:**
- Multi-store environments
- Different storefronts for different markets
- B2B vs B2C channel separation

### **6. Tax Management**

**Tax Classes:** `/v2/tax_classes`
- Product tax categorization
- Tax rate assignments

**Tax Rates:** `/v3/tax/rates`
- Geographic tax rate management
- Complex tax rule configuration

**Tax Zones:** `/v3/tax/zones`
- Geographic tax zone definitions
- Multi-jurisdiction tax support

**Tax Properties:** `/v3/tax/properties`
- Advanced tax configuration
- Custom tax calculations

### **7. Shipping**

**Current:** `/v3/shipping` | **Legacy:** `/v2/shipping`

**Features:**
- **Shipping Zones** - Geographic shipping areas
- **Shipping Methods** - Delivery options
- **Customs Information** - International shipping
- **Carrier Integration** - Third-party shipping services

### **8. Additional Entities**

**Currencies:** `/v2/currencies`
- Multi-currency support
- Exchange rate management

**Wishlists:** `/v3/wishlists`
- Customer wishlist management
- Wishlist item operations

**Subscribers:** `/v2/subscribers`
- Email subscription management
- Newsletter subscribers

---

## 🔧 Migration System Alignment

### **Entity-Level Configuration Support**
✅ **All Major Entities Support:**
- Batch operations for efficient bulk processing
- Individual operations for granular control
- Metafields for extended data capabilities
- Rate limiting compliance (12 req/sec)

### **Dependencies-First Processing Order**
✅ **Confirmed Processing Sequence:**
1. **Customer Groups** → **Customers**
2. **Brands** → **Products** 
3. **Categories/Category Trees** → **Products**
4. **Products** → **Variants** → **Images**
5. **Products** → **Modifiers** → **Values**
6. **Products** → **Options** → **Values**

### **Complex Product Handling**
✅ **Sub-Orchestration Support:**
- Products with 500+ variants fully supported
- Batch variant operations for efficiency
- Product images bulk management
- Product modifiers with values
- Parallel component processing

### **Multi-Level Cancellation Granularity**
✅ **API-Level Support:**
- **Migration Level** - All API endpoints
- **Entity Level** - Product, Category, Brand APIs
- **Component Level** - Variant, Image, Modifier APIs  
- **Batch Level** - Batch operation endpoints
- **Individual Level** - Single entity operations

### **Advanced Enterprise Features**
✅ **Production-Ready Capabilities:**
- **Webhooks** - Real-time event notifications for all entities
- **Metafields** - Extended data storage for all major entities
- **Multi-Channel** - Channel-specific configurations
- **Localization** - Currency and locale support
- **Bulk Operations** - Efficient large dataset processing

---

## 📈 API Version Strategy

### **Current Recommendations**
- **Products:** Use Catalog API (V3) ✅
- **Categories:** Use Category Trees (new) over deprecated Categories ✅
- **Customers:** Use Customers V3 over V2 ✅
- **Variants:** Use Catalog API with batch operations ✅
- **Orders:** Use Management API V3 ✅

### **Legacy Support**
- **V2 APIs** - Still functional but deprecated
- **Migration Strategy** - Use V3 where available, V2 for gaps
- **Future Proofing** - V3 APIs receive active development

---

## 🎯 Performance Optimization

### **Batch Operation Endpoints**
- **Products:** `PUT /v3/catalog/products` (batch update)
- **Variants:** `PUT /v3/catalog/products/{id}/variants` (batch update)  
- **Metafields:** Batch operations available for all major entities
- **Category Assignments:** Bulk product-category relationships

### **Pagination Strategies**
- **Cursor-based pagination** - V3 APIs (more efficient)
- **Offset-based pagination** - V2 APIs (legacy)
- **Maximum 250 items** per request for most endpoints

### **Webhook Integration**
- **Real-time monitoring** - Progress tracking via webhooks
- **Event-driven architecture** - Webhook notifications for all entities
- **Error detection** - Immediate notification of API failures

---

## 🔒 Security & Authentication

### **OAuth 2.0 Implementation**
- **Store-level API accounts** - Single store access
- **Account-level API accounts** - Multi-store access
- **Scoped permissions** - Granular access control

### **API Credentials**
- `client_id` - Application identifier
- `client_secret` - Secure authentication token
- `access_token` - Request authorization
- **API Path** - Base URL for API requests

---

## 📊 Scalability Metrics

### **Processing Capacity**
- **Small Scale:** 10K products in ~6 hours
- **Medium Scale:** 100K products in ~60 hours
- **Large Scale:** 10M products in 5-10 days
- **Complex Products:** 500+ variants/images without timeout

### **Rate Limit Optimization**
- **12 requests/second** maximum
- **Batch operations** reduce total API calls
- **Parallel processing** within rate limits
- **Exponential backoff** for retry logic

---

## 🚀 Future Considerations

### **Emerging Features**
- **GraphQL expansion** - More entities moving to GraphQL
- **Enhanced batch operations** - Larger batch sizes
- **Improved webhook coverage** - More granular events
- **Advanced filtering** - Better query capabilities

### **Deprecation Timeline**
- **V2 APIs** - Moving to maintenance mode
- **Legacy Categories** - Replaced by Category Trees
- **Blueprint themes** - Replaced by Stencil/Catalyst

---

## 📚 Additional Resources

### **Official Documentation**
- [REST Catalog API](https://developer.bigcommerce.com/docs/rest-catalog)
- [REST Management API](https://developer.bigcommerce.com/docs/rest-management)
- [GraphQL Storefront API](https://developer.bigcommerce.com/api-docs/storefront/graphql)
- [Webhook Events](https://developer.bigcommerce.com/api-docs/store-management/webhooks/events)

### **Developer Tools**
- [BigCommerce CLI](https://github.com/bigcommerce/bigcommerce-cli)
- [API Client Libraries](https://developer.bigcommerce.com/tools-resources)
- [Postman Collections](https://developer.bigcommerce.com/tools-resources)

---

**Document Version:** 1.0  
**Created:** January 2025  
**Next Review:** When BigCommerce releases major API updates

This document serves as the technical foundation for BigCommerce API integration in our migration system and should be referenced for all entity-specific implementation decisions. 