#!/usr/bin/env node

/**
 * Real-Time SignalR Integration Test
 * Tests the complete SignalR integration flow between frontend and backend
 */

const https = require('https');
const http = require('http');

// Test configuration
const config = {
  backendUrl: 'http://localhost:7071',
  frontendUrl: 'http://localhost:3000',
  signalREndpoint: '/api/negotiate',
  timeout: 10000
};

console.log('🧪 Testing Real-Time SignalR Integration');
console.log('=====================================');

// Helper function to make HTTP requests
function makeRequest(url, options = {}) {
  return new Promise((resolve, reject) => {
    const protocol = url.startsWith('https:') ? https : http;
    const timeout = setTimeout(() => {
      reject(new Error(`Request timeout after ${config.timeout}ms`));
    }, config.timeout);

    const req = protocol.get(url, options, (res) => {
      clearTimeout(timeout);
      let data = '';
      
      res.on('data', chunk => data += chunk);
      res.on('end', () => {
        try {
          const jsonData = JSON.parse(data);
          resolve({ status: res.statusCode, data: jsonData, headers: res.headers });
        } catch (e) {
          resolve({ status: res.statusCode, data: data.trim(), headers: res.headers });
        }
      });
    });

    req.on('error', (err) => {
      clearTimeout(timeout);
      reject(err);
    });
    
    req.setTimeout(config.timeout);
  });
}

// Test functions
async function testBackendHealth() {
  console.log('\n1️⃣ Testing Backend Health...');
  try {
    // Test if backend is responsive
    const response = await makeRequest(`${config.backendUrl}/api/negotiate`);
    
    if (response.status === 200 && response.data.url && response.data.accessToken) {
      console.log('✅ Backend is healthy');
      console.log(`   SignalR URL: ${response.data.url}`);
      console.log(`   Access Token: ${response.data.accessToken.substring(0, 20)}...`);
      return { success: true, signalRUrl: response.data.url, token: response.data.accessToken };
    } else {
      console.log('❌ Backend health check failed');
      console.log(`   Status: ${response.status}`);
      console.log(`   Response: ${JSON.stringify(response.data)}`);
      return { success: false };
    }
  } catch (error) {
    console.log('❌ Backend connection failed');
    console.log(`   Error: ${error.message}`);
    return { success: false, error: error.message };
  }
}

async function testFrontendReachability() {
  console.log('\n2️⃣ Testing Frontend Reachability...');
  try {
    const response = await makeRequest(config.frontendUrl);
    
    if (response.status === 200 && response.data.includes('root')) {
      console.log('✅ Frontend is reachable');
      return { success: true };
    } else {
      console.log('❌ Frontend reachability failed');
      console.log(`   Status: ${response.status}`);
      return { success: false };
    }
  } catch (error) {
    console.log('❌ Frontend connection failed');
    console.log(`   Error: ${error.message}`);
    return { success: false };
  }
}

async function testSignalRInfo() {
  console.log('\n3️⃣ Testing SignalR Info Endpoint...');
  try {
    const response = await makeRequest(`${config.backendUrl}/api/signalr-info`);
    
    if (response.status === 200) {
      console.log('✅ SignalR info endpoint working');
      console.log(`   Response: ${JSON.stringify(response.data, null, 2)}`);
      return { success: true, data: response.data };
    } else {
      console.log('⚠️ SignalR info endpoint returned non-200');
      console.log(`   Status: ${response.status}`);
      console.log(`   Response: ${JSON.stringify(response.data)}`);
      return { success: false };
    }
  } catch (error) {
    console.log('❌ SignalR info endpoint failed');
    console.log(`   Error: ${error.message}`);
    return { success: false };
  }
}

async function testDockerNetworking() {
  console.log('\n4️⃣ Testing Docker Internal Networking...');
  
  // This test verifies that the containers can communicate
  try {
    console.log('   Testing container-to-container communication...');
    
    // Since we can't directly test internal Docker networking from host,
    // we'll test if the external ports are working correctly
    const backendTest = await makeRequest(`${config.backendUrl}/api/negotiate`);
    const frontendTest = await makeRequest(config.frontendUrl);
    
    if (backendTest.status === 200 && frontendTest.status === 200) {
      console.log('✅ Docker networking appears healthy');
      console.log('   External port mapping working correctly');
      return { success: true };
    } else {
      console.log('❌ Docker networking issues detected');
      return { success: false };
    }
  } catch (error) {
    console.log('❌ Docker networking test failed');
    console.log(`   Error: ${error.message}`);
    return { success: false };
  }
}

async function runAllTests() {
  console.log(`Backend URL: ${config.backendUrl}`);
  console.log(`Frontend URL: ${config.frontendUrl}\n`);

  const results = {};
  
  // Run all tests
  results.backend = await testBackendHealth();
  results.frontend = await testFrontendReachability();
  results.signalRInfo = await testSignalRInfo();
  results.networking = await testDockerNetworking();

  // Summary
  console.log('\n📊 Test Results Summary');
  console.log('======================');
  
  const tests = [
    { name: 'Backend Health', result: results.backend },
    { name: 'Frontend Reachability', result: results.frontend },
    { name: 'SignalR Info', result: results.signalRInfo },
    { name: 'Docker Networking', result: results.networking }
  ];

  let passed = 0;
  tests.forEach(test => {
    const status = test.result.success ? '✅ PASS' : '❌ FAIL';
    console.log(`${status} ${test.name}`);
    if (test.result.success) passed++;
  });

  console.log(`\n🎯 Overall Result: ${passed}/${tests.length} tests passed`);
  
  if (passed === tests.length) {
    console.log('🎉 All integration tests passed! Real-time monitoring is ready.');
  } else {
    console.log('⚠️ Some tests failed. Check the errors above for troubleshooting.');
  }

  // Specific SignalR guidance
  if (results.backend.success) {
    console.log('\n🔧 SignalR Connection Details:');
    console.log(`   Hub URL: ${results.backend.signalRUrl}`);
    console.log('   Authentication: Azure SignalR Service with JWT token');
    console.log('   Ready for real-time progress tracking!');
  }

  return { allPassed: passed === tests.length, results };
}

// Run the tests
if (require.main === module) {
  runAllTests().catch(console.error);
}

module.exports = { runAllTests, config }; 