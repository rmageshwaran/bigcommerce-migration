# ✅ **MANDATORY PRE-WORK CHECKLIST**
## *Complete This Before ANY Development Work*

### 🎯 **PURPOSE**
This checklist ensures every change follows established architecture, patterns, and constraints. **NO EXCEPTIONS**.

---

## 📋 **STEP 1: UNDERSTAND THE REQUEST**

### **Classification** *(Check one)*
- [ ] **🔧 Bug Fix** - Fixing existing functionality
- [ ] **✨ New Feature** - Adding new capability  
- [ ] **⚡ Enhancement** - Improving existing feature
- [ ] **🏗️ Refactoring** - Code improvement without functional changes
- [ ] **📚 Documentation** - Documentation updates only

### **Impact Assessment** *(Check all that apply)*
- [ ] **SignalR Events** - Will create/modify SignalR events
- [ ] **Orchestrators** - Will modify Durable Functions orchestrators
- [ ] **API Integration** - Will change BigCommerce API interactions
- [ ] **Database/Storage** - Will modify data storage patterns
- [ ] **Performance** - May impact system performance
- [ ] **Error Handling** - Will modify error handling patterns
- [ ] **Testing** - Will require new/updated tests

---

## 📚 **STEP 2: REQUIRED READING** 

### **ALWAYS READ** *(Mandatory for all work)*
- [ ] **`MASTER-ARCHITECTURE-INDEX.md`** - Complete constraints overview
- [ ] **`Architecture-Documentation.md`** - System architecture understanding

### **CONDITIONAL READING** *(Based on impact assessment)*

#### **IF SignalR Events Involved:**
- [ ] **`SignalR-Centralized-Architecture.md`** - Centralization patterns
- [ ] **`SignalR-Centralization-QUICK-REFERENCE.md`** - Factory usage patterns

#### **IF Orchestrators Involved:**
- [ ] **`Azure-Durable-Functions-Deterministic-Architecture.md`** - Determinism requirements
- [ ] **`Consistent-Cancellation-Handling-Implementation.md`** - Error handling patterns

#### **IF API Integration Involved:**
- [ ] **`BigCommerce-API-Entity-Reference.md`** - API capabilities and limits
- [ ] **`Feature-Enhancement-Distributed-Rate-Limiting.md`** - Rate limiting patterns

#### **IF Performance Impact:**
- [ ] **`Throughput-Optimization-Technical-Architecture.md`** - Performance architecture
- [ ] **`Performance-Optimization-Completion-Summary.md`** - Current optimizations

#### **IF Testing Required:**
- [ ] **`Testing-Strategy-Guide.md`** - Testing approach
- [ ] **`Coding-Standards-and-Testing-Strategy.md`** - TDD requirements

---

## 🔍 **STEP 3: PATTERN VERIFICATION**

### **Check Existing Codebase** *(Use codebase_search)*
- [ ] **Search for similar functionality** - Look for existing patterns to follow
- [ ] **Check current implementations** - Understand how similar features work
- [ ] **Validate naming conventions** - Follow established naming patterns
- [ ] **Review test patterns** - Understand testing approach for similar code

### **Example Searches to Run:**
```
// For SignalR work
"How does SignalR event creation work in the migration system?"

// For orchestrator work  
"How are Durable Functions orchestrators implemented?"

// For API integration
"How does BigCommerce API integration work with rate limiting?"

// For error handling
"How is error handling implemented in orchestrators?"
```

---

## 🏗️ **STEP 4: ARCHITECTURE COMPLIANCE**

### **Validate Against Core Principles**
- [ ] **Azure Durable Functions Determinism**: Will not break deterministic execution
- [ ] **SignalR Centralization**: Will use SignalREventFactory (never manual events)
- [ ] **Continue-on-Error**: Will not stop migration for individual failures
- [ ] **No Retry Logic**: Will not add automatic retry mechanisms
- [ ] **Unit Testability**: Will be fully testable with dependency injection
- [ ] **Performance Requirements**: Will not degrade system performance

### **Memory Constraints**
- [ ] **Azure Functions Limits**: Will not exceed memory limits
- [ ] **Rate Limiting**: Will respect 5-50 req/sec dynamic limits
- [ ] **Error Rate Target**: Will maintain <5% error rate requirement

---

## 🧪 **STEP 5: TESTING STRATEGY**

### **Test Planning** *(TDD Approach)*
- [ ] **Write test cases FIRST** - Define expected behavior before implementation
- [ ] **Plan test data** - Define test scenarios and data requirements
- [ ] **Mock dependencies** - Plan how to mock external dependencies
- [ ] **Performance testing** - Plan performance validation if applicable

### **Test Coverage Requirements**
- [ ] **Unit Tests**: 99%+ coverage for new code
- [ ] **Integration Tests**: If modifying service interactions
- [ ] **E2E Tests**: If modifying complete workflows
- [ ] **Performance Tests**: If performance impact expected

---

## 💻 **STEP 6: IMPLEMENTATION CHECKLIST**

### **Before Writing Code**
- [ ] **Set up TDD environment** - Write failing tests first
- [ ] **Plan dependency injection** - Define interface requirements
- [ ] **Review error handling** - Plan exception scenarios
- [ ] **Check SignalR requirements** - Plan event creation if needed

### **During Implementation**
- [ ] **Follow established patterns** - Use existing code patterns as reference
- [ ] **Use proper error handling** - Return results, don't throw in Durable Functions
- [ ] **Implement logging** - Add appropriate logging for debugging
- [ ] **Add comprehensive tests** - Maintain 99%+ test coverage

---

## ✅ **STEP 7: COMPLETION VALIDATION**

### **Before Submitting**
- [ ] **All tests pass**: 562+ tests must continue passing
- [ ] **No breaking changes**: Backward compatibility maintained
- [ ] **Performance validated**: No degradation in system performance
- [ ] **Documentation updated**: Relevant docs updated if needed
- [ ] **Code review ready**: Code follows all established patterns

### **Final Checks**
- [ ] **SignalR events use factory**: No manual event creation
- [ ] **Error handling consistent**: Proper result patterns used
- [ ] **Dependencies injected**: No hard dependencies
- [ ] **Tests comprehensive**: Edge cases covered
- [ ] **Performance impact assessed**: No negative impact on throughput

---

## 🚨 **CRITICAL REMINDERS**

### **NEVER DO:**
❌ Start coding without completing this checklist
❌ Create SignalR events manually (use factory)
❌ Throw exceptions in Durable Functions components
❌ Add retry logic for API calls
❌ Skip writing tests
❌ Break existing functionality

### **ALWAYS DO:**
✅ Complete this entire checklist first
✅ Read all relevant documentation
✅ Follow TDD approach
✅ Use dependency injection
✅ Maintain backward compatibility
✅ Validate performance impact

---

## 📊 **COMPLETION SIGNATURE**

**Work Type**: _________________  
**Impact Areas**: _________________  
**Documents Read**: _________________  
**Tests Planned**: _________________  
**Date Completed**: _________________  

**✅ I have completed ALL items in this checklist and am ready to begin implementation following established patterns and constraints.**

---

**🎯 REMEMBER**: The system currently has 85% completion with 562/562 tests passing (100% success rate). Any changes must maintain this high standard and follow all established architectural patterns. 