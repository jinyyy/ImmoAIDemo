# 🏠 Immo24 AI Applicant Scorer – Setup & Run Guide (VS Code)

Eine KI-gestützte .NET 8/9 Web-Applikation (Blazor Interactive Server + Minimal API) zur automatisierten Bewertung und Priorisierung von Immobilien-Bewerberprofilen.

---

## 📋 Voraussetzungen (Prerequisites)

Stelle sicher, dass folgende Tools auf deinem System installiert sind:
1. **[.NET 8.0 oder 9.0 SDK](https://dotnet.microsoft.com/download)** (Prüfen mit `dotnet --version` im Terminal)
2. **[Visual Studio Code](https://code.visualstudio.com/)**
3. **VS Code Extension:** [C# Dev Kit](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit) (empfohlen für IntelliSense & Debugging)

---

## 🛠️ Option 1: Bestehendes Repository klonen & starten

Wenn du dieses Repository direkt ausführen möchtest:

```bash
# 1. Repository klonen
git clone https://github.com/jinyyy/ImmoAIDemo.git
cd ImmoAiDemo

# 2. In VS Code öffnen
code .

# 3. Abhängigkeiten wiederherstellen & App mit Hot-Reload starten
dotnet watch
