# AgentCompanyWeb

**Built: January 2026** | Archived

A real-time agent management dashboard and orchestration platform built with Blazor Server. Designed to manage fleets of AI agents — assign tasks, monitor status, coordinate teams, and handle event-driven workflows through a command-center-style UI.

## What It Does

- **Agent Management** — Register, monitor, and control AI agents with real-time status updates via SignalR
- **Team Organization** — Group agents into teams with color-coded visual organization and a drag-and-drop canvas
- **Task Assignment** — Assign tasks to individual agents or teams through a modal interface with priority levels
- **Cron Scheduling** — Schedule recurring jobs with cron expressions, targeting agents or teams
- **GitHub Webhooks** — Receive GitHub events (push, PR, issues, etc.) and route them as messages to agents/teams
- **Event Triggers** — Define custom triggers that fire on agent status changes, messages, or webhook events
- **Live Dashboard** — Command center with swarm minimap, activity timeline, alerts, and active work panels
- **Inter-Agent Messaging** — Direct and group messaging system with persistent history
- **Credential Management** — Encrypted storage for API keys and tokens per group
- **Docker Integration** — Scale agent containers up/down through the UI

## Tech Stack

- **Framework:** ASP.NET (.NET 10) / Blazor Server with interactive SSR
- **Real-time:** SignalR (WebSocket hub for agent communication)
- **Database:** SQLite via Entity Framework Core
- **Frontend:** Razor Components, Bootstrap 5, custom CSS
- **Services:** Background hosted services for cron scheduling and trigger execution
- **Encryption:** AES-256 for credential storage

## Running Locally

```bash
dotnet restore
dotnet run
```

The app starts on `http://localhost:5050` by default.

Requires the companion `AgentCompanyEngineer` project (referenced as a project dependency) which provides Redis, Docker, and agent network services.

## License

MIT

## Author

NodeNestor
