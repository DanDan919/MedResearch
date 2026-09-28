import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  allowedDevOrigins: ["127.0.0.1"],
  transpilePackages: ["@medresearch/api", "@medresearch/ui"]
};

export default nextConfig;
