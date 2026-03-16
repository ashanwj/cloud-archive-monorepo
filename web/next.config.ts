import type { NextConfig } from 'next'

// Locally: falls back to the .NET dev server
// In Amplify: set by CDK to the App Runner service URL
const apiBase = process.env.DOTNET_API_URL ?? 'http://localhost:54331'

const nextConfig: NextConfig = {
  async rewrites() {
    return [
      {
        source:      '/api/:path*',
        destination: `${apiBase}/api/:path*`,
      },
    ]
  },
}

export default nextConfig
