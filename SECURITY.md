# Security Policy

## Scope

This is a **proof-of-concept** project intended for local development and demonstration purposes only. It is **not** designed for production deployment.

Key points:
- All LLM inference runs locally via Ollama — **no data leaves your machine**
- The API has no authentication (CORS is fully open)
- The app binds to `localhost` only by default

**Do not expose this app to the public internet without adding authentication.**

## Reporting a vulnerability

If you discover a security issue in this codebase, please open a [GitHub Issue](../../issues) labelled `security`. For sensitive disclosures, contact the maintainers directly via GitHub's private vulnerability reporting feature.

Please include:
- A description of the vulnerability
- Steps to reproduce
- Potential impact
- Any suggested mitigations

We aim to respond within 5 business days.

## Out of scope

- Vulnerabilities in Ollama itself — report those to the [Ollama project](https://github.com/ollama/ollama)
- Vulnerabilities in Semantic Kernel — report those to [Microsoft](https://github.com/microsoft/semantic-kernel/security)
- Issues arising from deploying this POC in a production environment without hardening
