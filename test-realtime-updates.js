#!/usr/bin/env node

const http = require('http');

const MIGRATION_ID = 'c870b725-4923-4b76-8d39-add2e3e56a53';
const API_BASE = 'http://localhost:7071';
const API_KEY = 'your-api-key-here';

// Simulate migration progress
let currentProgress = {
  totalEntities: 284,
  processedEntities: 40,
  successfulEntities: 40,
  failedEntities: 0,
  overallProgressPercentage: 14.1,
  currentBatch: 3,
  totalBatches: 15,
  currentEntity: 'categories',
  estimatedTimeRemaining: '12 minutes'
};

const entityTypes = ['categories', 'products', 'brands', 'customers'];
const phases = ['fetching', 'transforming', 'creating', 'validating'];

async function sendRequest(url, data) {
  return new Promise((resolve, reject) => {
    const postData = JSON.stringify(data);
    
    const options = {
      hostname: 'localhost',
      port: 7071,
      path: url.replace('http://localhost:7071', ''),
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'x-functions-key': API_KEY,
        'Content-Length': Buffer.byteLength(postData)
      }
    };

    const req = http.request(options, (res) => {
      let responseData = '';
      res.on('data', (chunk) => {
        responseData += chunk;
      });
      res.on('end', () => {
        resolve({ status: res.statusCode, data: responseData });
      });
    });

    req.on('error', (error) => {
      reject(error);
    });

    req.write(postData);
    req.end();
  });
}

async function sendMigrationProgress() {
  console.log(`📊 Sending progress update: ${currentProgress.processedEntities}/${currentProgress.totalEntities} (${currentProgress.overallProgressPercentage.toFixed(1)}%)`);
  
  try {
    // Using the actual SignalR endpoint for status updates
    const response = await sendRequest(`${API_BASE}/api/signalr/status-update`, {
      migrationId: MIGRATION_ID,
      status: 'processing',
      progress: currentProgress,
      timestamp: new Date().toISOString()
    });
    
    console.log(`✅ Progress sent - Status: ${response.status}`);
  } catch (error) {
    console.error(`❌ Failed to send progress:`, error.message);
  }
}

async function sendMigrationStatusUpdate() {
  console.log(`🔄 Sending migration status update`);
  
  try {
    // Using the actual migration progress endpoint
    const response = await sendRequest(`${API_BASE}/api/signalr/migration-progress`, {
      migrationId: MIGRATION_ID,
      progress: {
        migrationId: MIGRATION_ID,
        status: 'inprogress',
        lastUpdated: new Date().toISOString(),
        totalEntities: currentProgress.totalEntities,
        processedEntities: currentProgress.processedEntities,
        successfulEntities: currentProgress.successfulEntities,
        failedEntities: currentProgress.failedEntities,
        overallProgressPercentage: currentProgress.overallProgressPercentage,
        estimatedTimeRemaining: currentProgress.estimatedTimeRemaining
      },
      timestamp: new Date().toISOString()
    });
    
    console.log(`✅ Migration progress sent - Status: ${response.status}`);
  } catch (error) {
    console.error(`❌ Failed to send migration progress:`, error.message);
  }
}

async function sendBatchUpdate() {
  const currentEntityType = entityTypes[Math.floor(Math.random() * entityTypes.length)];
  
  console.log(`🔄 Sending batch completion: ${currentEntityType} (batch ${currentProgress.currentBatch})`);
  
  try {
    const response = await sendRequest(`${API_BASE}/api/signalr/batch-completion`, {
      migrationId: MIGRATION_ID,
      entityType: currentEntityType,
      batchNumber: currentProgress.currentBatch,
      results: {
        batchSize: 20,
        processedCount: Math.floor(Math.random() * 20) + 1,
        successCount: Math.floor(Math.random() * 18) + 1,
        failureCount: Math.floor(Math.random() * 2),
        message: `Completed batch ${currentProgress.currentBatch} for ${currentEntityType}`
      },
      timestamp: new Date().toISOString()
    });
    
    console.log(`✅ Batch update sent - Status: ${response.status}`);
  } catch (error) {
    console.error(`❌ Failed to send batch update:`, error.message);
  }
}

async function sendEntityStart() {
  const currentEntityType = entityTypes[Math.floor(Math.random() * entityTypes.length)];
  
  console.log(`🚀 Sending entity start: ${currentEntityType}`);
  
  try {
    const response = await sendRequest(`${API_BASE}/api/signalr/entity-start`, {
      migrationId: MIGRATION_ID,
      entityType: currentEntityType,
      totalCount: Math.floor(Math.random() * 100) + 50,
      timestamp: new Date().toISOString()
    });
    
    console.log(`✅ Entity start sent - Status: ${response.status}`);
  } catch (error) {
    console.error(`❌ Failed to send entity start:`, error.message);
  }
}

async function sendEntityPhaseStart() {
  const currentEntityType = entityTypes[Math.floor(Math.random() * entityTypes.length)];
  const currentPhase = phases[Math.floor(Math.random() * phases.length)];
  
  console.log(`⚙️ Sending entity phase start: ${currentPhase} ${currentEntityType}`);
  
  try {
    const response = await sendRequest(`${API_BASE}/api/signalr/entity-phase-start`, {
      migrationId: MIGRATION_ID,
      entityType: currentEntityType,
      phaseData: {
        phase: currentPhase,
        message: `Starting ${currentPhase} phase for ${currentEntityType}`,
        estimatedDuration: '2-3 minutes'
      },
      timestamp: new Date().toISOString()
    });
    
    console.log(`✅ Entity phase start sent - Status: ${response.status}`);
  } catch (error) {
    console.error(`❌ Failed to send entity phase start:`, error.message);
  }
}

async function sendError() {
  // Occasionally send an error (10% chance)
  if (Math.random() < 0.1) {
    console.log(`⚠️ Sending error notification`);
    
    try {
      const response = await sendRequest(`${API_BASE}/api/signalr/error-notification`, {
        migrationId: MIGRATION_ID,
        errorData: {
          errorType: 'RateLimitExceeded',
          message: 'API rate limit exceeded, retrying in 30 seconds...',
          retryable: true,
          retryDelay: 30,
          timestamp: new Date().toISOString()
        },
        timestamp: new Date().toISOString()
      });
      
      console.log(`✅ Error sent - Status: ${response.status}`);
    } catch (error) {
      console.error(`❌ Failed to send error:`, error.message);
    }
  }
}

async function sendSystemHealth() {
  // Occasionally send system health (20% chance)
  if (Math.random() < 0.2) {
    console.log(`💓 Sending system health update`);
    
    try {
      const response = await sendRequest(`${API_BASE}/api/signalr/system-health`, {
        healthData: {
          status: 'healthy',
          activeMigrations: 1,
          queuedMigrations: 0,
          systemLoad: Math.floor(Math.random() * 30) + 20,
          memoryUsage: Math.floor(Math.random() * 40) + 40,
          timestamp: new Date().toISOString()
        },
        timestamp: new Date().toISOString()
      });
      
      console.log(`✅ System health sent - Status: ${response.status}`);
    } catch (error) {
      console.error(`❌ Failed to send system health:`, error.message);
    }
  }
}

function updateProgress() {
  // Simulate progress increment
  const increment = Math.floor(Math.random() * 3) + 1;
  currentProgress.processedEntities = Math.min(currentProgress.processedEntities + increment, currentProgress.totalEntities);
  currentProgress.successfulEntities = currentProgress.processedEntities - currentProgress.failedEntities;
  currentProgress.overallProgressPercentage = (currentProgress.processedEntities / currentProgress.totalEntities) * 100;
  
  // Update batch progress
  if (currentProgress.processedEntities % 20 === 0) {
    currentProgress.currentBatch++;
  }
  
  // Occasionally add a failure
  if (Math.random() < 0.05) {
    currentProgress.failedEntities++;
    currentProgress.successfulEntities = currentProgress.processedEntities - currentProgress.failedEntities;
  }
  
  // Update estimated time
  const remaining = currentProgress.totalEntities - currentProgress.processedEntities;
  const estimatedMinutes = Math.ceil(remaining / 3); // Assuming 3 entities per minute
  currentProgress.estimatedTimeRemaining = `${estimatedMinutes} minutes`;
  
  // Check if completed
  if (currentProgress.processedEntities >= currentProgress.totalEntities) {
    console.log(`🎉 Migration completed!`);
    return false; // Stop the simulation
  }
  
  return true; // Continue simulation
}

async function simulateRealTimeUpdates() {
  console.log(`🚀 Starting real-time migration simulation for: ${MIGRATION_ID}`);
  console.log(`📱 Monitor at: http://localhost:3000/realtime?migrationId=${MIGRATION_ID}`);
  console.log(`⏰ Updates every 3 seconds, press Ctrl+C to stop\n`);
  
  let updateCount = 0;
  
  const interval = setInterval(async () => {
    updateCount++;
    console.log(`\n📡 Update #${updateCount} - ${new Date().toLocaleTimeString()}`);
    
    // Send core migration progress
    await sendMigrationProgress();
    await sendMigrationStatusUpdate();
    
    // Send various types of updates occasionally
    if (Math.random() < 0.6) {
      await sendBatchUpdate();
    }
    
    if (Math.random() < 0.3) {
      await sendEntityStart();
    }
    
    if (Math.random() < 0.4) {
      await sendEntityPhaseStart();
    }
    
    // Send errors occasionally
    await sendError();
    
    // Send system health occasionally
    await sendSystemHealth();
    
    // Update progress for next iteration
    const shouldContinue = updateProgress();
    
    if (!shouldContinue) {
      console.log(`\n🏁 Simulation completed!`);
      clearInterval(interval);
      
      // Send final completion message
      try {
        await sendRequest(`${API_BASE}/api/signalr/migration-completed`, {
          migrationId: MIGRATION_ID,
          data: {
            status: 'completed',
            totalEntities: currentProgress.totalEntities,
            processedEntities: currentProgress.processedEntities,
            successfulEntities: currentProgress.successfulEntities,
            failedEntities: currentProgress.failedEntities,
            duration: '15 minutes',
            message: 'Migration completed successfully!'
          },
          timestamp: new Date().toISOString()
        });
        console.log(`✅ Completion message sent`);
      } catch (error) {
        console.error(`❌ Failed to send completion:`, error.message);
      }
    }
    
    console.log('---');
  }, 3000); // Update every 3 seconds
}

// Handle graceful shutdown
process.on('SIGINT', () => {
  console.log('\n🛑 Stopping simulation...');
  process.exit(0);
});

// Start the simulation
simulateRealTimeUpdates().catch(console.error); 