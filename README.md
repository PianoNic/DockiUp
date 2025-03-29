<p align="center">
    <img src="assets/DockiUpLogo.png" width="800" alt="DockiUp Logo">
</p>
<p align="center">
    <strong>DockiUp</strong> - Git-based Docker container deployment automation
</p>
<p align="center">
    You commit, we pull, build, and deploy.
</p>
<p align="center">
    <a href="#"><img src="https://img.shields.io/badge/Documentation-Docs-006db8.svg" alt="Documentation"/></a>
    <a href="#"><img src="https://img.shields.io/badge/Selfhost-Instructions-006db8.svg" alt="Self-hosting"/></a>
    <a href="#"><img src="https://img.shields.io/badge/Development-Setup-006db8.svg" alt="Development"/></a>
</p>

---

## 🚀 What is DockiUp?

DockiUp monitors your Git repositories for changes, then automatically:
1. Pulls the latest changes from your specified branch
2. Rebuilds your Docker containers with the updated code
3. Deploys the new containers with minimal downtime

Perfect for developers who want a simple CI/CD pipeline for their personal projects and self-hosted services.

## 🖼️ Screenshots

<p align="center">
    <img src="assets/dashboard-screenshot.png" width="80%" alt="DockiUp Dashboard">
    <em>Main dashboard showing monitored repositories and their status</em>
</p>

<p align="center">
    <img src="assets/config-screenshot.png" width="80%" alt="Repository Configuration">
    <em>Repository configuration screen with update interval settings</em>
</p>

<p align="center">
    <img src="assets/logs-screenshot.png" width="80%" alt="Deployment Logs">
    <em>Deployment logs showing successful pull, build, and restart</em>
</p>

## ✨ Features

- **Automatic Monitoring**: Checks for Git repository changes at customizable intervals
- **Manual Trigger**: Force updates on demand via the intuitive web interface
- **Multi-Repository Support**: Manage multiple projects from a single dashboard
- **Flexible Configuration**:
  - Set custom check intervals (from 5 minutes to daily)
  - Specify which Git branch to monitor
  - Configure rebuild parameters
- **Authentication Support**: Works with both public and private Git repositories

## 🔧 How It Works

DockiUp runs as a service that:
1. Monitors your specified Git repositories for new commits
2. When changes are detected, pulls the latest code
3. Rebuilds Docker containers based on the updated code
4. Restarts the containers with your specified configuration

<p align="center">
    <img src="assets/workflow-diagram.png" width="90%" alt="DockiUp Workflow">
    <em>DockiUp workflow diagram</em>
</p>

## 📋 Getting Started

1. **Installation**: [Documentation](#) for setup instructions
2. **Configuration**: Add your Git repositories and Docker settings
3. **Monitor**: Watch your containers stay up-to-date automatically

<p align="center">
    <img src="assets/setup-screenshot.png" width="80%" alt="Setup Screen">
    <em>Initial setup and repository configuration</em>
</p>

## 💻 Technical Details

- DockiUp requires access to the Docker socket to manage containers
- Runs either as a standalone application or as a Docker container itself
- Built with C# backend and modern frontend for simple management
- Supports webhook integration (coming soon) for instant updates

## 📜 License

This project is licensed under the GPL-3.0 License.  
See the [LICENSE](LICENSE) file for details.

---
<p align="center">Made with ❤️ by <a href="https://github.com/Pianonic">PianoNic</a></p>
