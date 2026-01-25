# 🤖 **AI ASSISTANT WORKFLOW GUIDE**
## *Systematic Approach to BigCommerce Migration System Development*

### 🎯 **PURPOSE**
This guide ensures that AI assistants **ALWAYS** follow established patterns, reference relevant documentation, and maintain architectural consistency for ALL work on the BigCommerce Migration System.

---

## 📋 **MANDATORY WORKFLOW FOR ALL REQUESTS**

### **🔍 STEP 1: INITIAL REQUEST ANALYSIS**

#### **Request Classification** *(Determine before proceeding)*
- [ ] **🔧 Bug Fix** - Fixing existing functionality
- [ ] **✨ New Feature** - Adding new capability
- [ ] **⚡ Enhancement** - Improving existing feature
- [ ] **🏗️ Refactoring** - Code improvement without functional changes
- [ ] **📚 Documentation** - Documentation updates
- [ ] **🧪 Testing** - Test-related work
- [ ] **🚀 Performance** - Performance optimization

#### **Impact Assessment** *(Check all applicable areas)*
- [ ] **SignalR Events** - Will affect real-time communication
- [ ] **Orchestrators** - Will modify Durable Functions orchestrators
- [ ] **API Integration** - Will change BigCommerce API interactions
- [ ] **Database/Storage** - Will modify data storage patterns
- [ ] **Performance** - May impact system performance
- [ ] **Error Handling** - Will modify error handling patterns
- [ ] **Testing** - Will require new/updated tests
- [ ] **Frontend** - Will affect React dashboard
- [ ] **Infrastructure** - Will change deployment or configuration

---

## 📚 **STEP 2: REQUIRED DOCUMENTATION REVIEW**

### **ALWAYS READ FIRST** *(Mandatory for ALL requests)*
1. **`MASTER-ARCHITECTURE-INDEX.md`** - Complete system constraints
2. **`PRE-WORK-CHECKLIST.md`** - Mandatory development checklist
3. **`CODING-PATTERNS-QUICK-REFERENCE.md`** - Copy-paste ready patterns

### **CONDITIONAL READING** *(Based on impact assessment)*

#### **IF SignalR Events Involved:**
```bash
# Required reading
- SignalR-Centralized-Architecture.md
- SignalR-Centralization-QUICK-REFERENCE.md
- SignalR-Frontend-Integration-Guide.md

# Search queries to run
"How does SignalR event creation work in the migration system?"
"What are the centralized SignalR patterns?"
```

#### **IF Orchestrators Involved:**
```bash
# Required reading
- Azure-Durable-Functions-Deterministic-Architecture.md
- Consistent-Cancellation-Handling-Implementation.md
- Migration-Architecture-and-Execution-Flow.md

# Search queries to run
"How are Durable Functions orchestrators implemented?"
"What are the deterministic orchestrator requirements?"
```

#### **IF API Integration Involved:**
```bash
# Required reading
- BigCommerce-API-Entity-Reference.md
- Feature-Enhancement-Distributed-Rate-Limiting.md
- Throughput-Optimization-Technical-Architecture.md

# Search queries to run
"How does BigCommerce API integration work with rate limiting?"
"What are the API integration patterns?"
```

#### **IF Performance Impact:**
```bash
# Required reading
- Performance-Optimization-Completion-Summary.md
- Throughput-Optimization-Technical-Architecture.md
- Performance-Optimization-Detailed-Task-Breakdown.md

# Search queries to run
"What are the performance optimization patterns?"
"How is system performance validated?"
```

#### **IF Testing Required:**
```bash
# Required reading
- Testing-Strategy-Guide.md
- Coding-Standards-and-Testing-Strategy.md
- Integration-and-E2E-Testing-Strategy.md

# Search queries to run
"What are the testing patterns for [component type]?"
"How is TDD implemented in the system?"
```

---

## 🔍 **STEP 3: CODEBASE PATTERN DISCOVERY**

### **Required Searches** *(Use codebase_search tool)*

#### **Pattern Discovery Searches:**
```bash
# General functionality understanding
"How does [specific functionality] work in the migration system?"
"Where is [component name] implemented in the codebase?"

# Pattern identification
"How are [entity type] entities processed?"
"What are the error handling patterns in [component type]?"
"How is dependency injection configured for [service type]?"

# Architecture validation
"How does [similar feature] integrate with the existing architecture?"
"What are the performance patterns for [operation type]?"
```

#### **Use Parallel Tool Calls** *(For efficiency)*
```bash
# Example: For SignalR + Orchestrator work
codebase_search("How does SignalR event creation work?")
codebase_search("How are orchestrators implemented?") 
codebase_search("What are the error handling patterns?")
grep_search("SignalREventFactory")
read_file("relevant_file_from_searches.cs")
```

---

## 🏗️ **STEP 4: ARCHITECTURE COMPLIANCE VALIDATION**

### **Validate Against Critical Constraints** *(Reference memory: 4674884)*

#### **Azure Durable Functions Determinism:**
- [ ] **No external calls in orchestrators** - Use activities only
- [ ] **No throwing exceptions** - Return result objects instead
- [ ] **Deterministic execution** - No random values, external dependencies

#### **SignalR Centralization:**
- [ ] **Use SignalREventFactory** - Never create events manually
- [ ] **Auto-populated properties** - Factory handles base properties
- [ ] **Consistent naming** - Follow established event patterns

#### **Continue-on-Error Policy:**
- [ ] **Individual failures OK** - Don't stop migration for single entity failures
- [ ] **Comprehensive logging** - Log all errors with context
- [ ] **Progress continuation** - Continue processing other entities

#### **Performance Requirements:**
- [ ] **Throughput target** - Maintain 12,000+ req/hour
- [ ] **Error rate target** - Keep <5% error rate
- [ ] **Rate limiting** - Respect dynamic 5-50 req/sec limits
- [ ] **Memory limits** - Stay within Azure Functions constraints

#### **Testing Standards:**
- [ ] **TDD approach** - Write tests first
- [ ] **99%+ coverage** - Comprehensive test coverage
- [ ] **562+ tests passing** - Maintain test suite integrity

---

## 💻 **STEP 5: IMPLEMENTATION APPROACH**

### **Use Established Patterns** *(Reference CODING-PATTERNS-QUICK-REFERENCE.md)*

#### **SignalR Events:**
```csharp
// ✅ ALWAYS use factory pattern
var progressEvent = _signalREventFactory.CreateMigrationProgress(migrationId, options);
```

#### **Error Handling:**
```csharp
// ✅ Durable Functions - return result objects
catch (OperationCanceledException)
{
    return new BatchProcessingResult { IsSuccess = false, ErrorMessage = "Cancelled" };
}
```

#### **Testing:**
```csharp
// ✅ TDD approach - write tests first
[Test]
public async Task Method_WithCondition_ExpectedBehavior()
{
    // Arrange, Act, Assert pattern
}
```

### **Follow TODO Management:**
- [ ] **Create TODOs** for complex multi-step work
- [ ] **Update progress** as work progresses
- [ ] **Mark complete** when tasks are finished

---

## ✅ **STEP 6: VALIDATION AND COMPLETION**

### **Before Submitting Work:**

#### **Architecture Compliance:**
- [ ] **No manual SignalR events** - Factory used everywhere
- [ ] **Proper error handling** - Result objects returned
- [ ] **Dependency injection** - All services properly injected
- [ ] **Performance impact** - No degradation introduced

#### **Code Quality Standards:**
- [ ] **SOLID Principles Applied** - Single Responsibility, Open/Closed, Liskov Substitution, Interface Segregation, Dependency Inversion
- [ ] **Complete XML Documentation** - All public classes, methods, properties, and parameters documented
- [ ] **Consistent Naming** - Follow established naming conventions
- [ ] **Clean Code** - No code smells, proper separation of concerns

#### **Testing Requirements:**
- [ ] **Tests written first** - TDD approach followed
- [ ] **Comprehensive coverage** - Edge cases included
- [ ] **All tests pass** - No regression introduced
- [ ] **Performance validated** - No negative impact

#### **Documentation:**
- [ ] **Patterns followed** - Established patterns used
- [ ] **Documentation updated** - If new patterns introduced
- [ ] **Memory updated** - If architectural changes made

---

## 🧠 **MEMORY SYSTEM USAGE**

### **Key Memory References:**
- **Memory 4674884**: Core architectural constraints and patterns
- **Memory 4610425**: SignalR centralization project status and completion
- **Memory 4493720**: User preferences for deployment commands
- **Memory 4493718**: Technical workarounds for macOS terminal issues

### **When to Update Memory:**
- [ ] **New architectural patterns** - Document new patterns established
- [ ] **User corrections** - Update memory when user corrects information
- [ ] **Constraint changes** - Update if requirements change
- [ ] **Completion status** - Update project completion status

---

## 🚨 **CRITICAL FAILURE PREVENTION**

### **NEVER DO:**
❌ Start work without completing this workflow
❌ Skip documentation review
❌ Ignore established patterns
❌ Create SignalR events manually
❌ Throw exceptions in Durable Functions
❌ Add retry logic for API calls
❌ Skip writing tests
❌ Break backward compatibility

### **ALWAYS DO:**
✅ Complete entire workflow for every request
✅ Read all relevant documentation
✅ Use parallel tool calls for efficiency
✅ Follow established patterns exactly
✅ Use SignalREventFactory for all events
✅ Return result objects from Durable Functions
✅ Write tests first (TDD)
✅ Apply SOLID principles in all code
✅ Add complete XML documentation during generation
✅ Validate performance impact
✅ Update memory when needed

---

## 📊 **WORKFLOW COMPLETION CHECKLIST**

### **For Each Request:**
- [ ] **Step 1**: Request analyzed and classified
- [ ] **Step 2**: All relevant documentation reviewed
- [ ] **Step 3**: Codebase patterns discovered using searches
- [ ] **Step 4**: Architecture compliance validated
- [ ] **Step 5**: Implementation follows established patterns
- [ ] **Step 6**: Work validated and completed properly

### **Quality Gates:**
- [ ] **Documentation Review**: All relevant docs read
- [ ] **Pattern Discovery**: Existing patterns identified and followed
- [ ] **Architecture Compliance**: All constraints respected
- [ ] **SOLID Principles**: Single Responsibility, Open/Closed, Liskov Substitution, Interface Segregation, Dependency Inversion applied
- [ ] **XML Documentation**: Complete documentation for all public members during code generation
- [ ] **Testing Standards**: TDD approach with 99%+ coverage
- [ ] **Performance Validation**: No negative performance impact
- [ ] **Backward Compatibility**: No breaking changes introduced

---

**🎯 REMEMBER**: 
- **Current Status**: 85% complete, 562/562 tests passing, production-ready
- **Core Principle**: Follow established patterns, maintain backward compatibility
- **Quality Standard**: Enterprise-grade with comprehensive testing

**📚 WORKFLOW REFERENCES**:
- `MASTER-ARCHITECTURE-INDEX.md` - Complete system overview
- `PRE-WORK-CHECKLIST.md` - Development checklist  
- `CODING-PATTERNS-QUICK-REFERENCE.md` - Ready-to-use patterns

This workflow ensures every AI assistant interaction maintains the high quality and architectural consistency that has been established in this enterprise-grade migration system. 