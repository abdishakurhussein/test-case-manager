// Browser tests can use an isolated API port; normal development uses 5084.
module.exports = {
  '/api/**': {
    target: process.env.API_PROXY_TARGET || 'http://localhost:5084',
    secure: false,
  },
};
