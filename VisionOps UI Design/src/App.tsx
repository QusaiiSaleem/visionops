import { useState } from "react";
import { Camera, Activity, Database, User, Settings } from "lucide-react";
import { Button } from "./components/ui/button";
import { Card } from "./components/ui/card";
import { CameraAccess } from "./components/CameraAccess";
import { FrameMonitoring } from "./components/FrameMonitoring";
import { DataAnalysis } from "./components/DataAnalysis";
import { UserSettings } from "./components/UserSettings";

type ActiveView = "cameras" | "monitoring" | "analysis" | "settings";

export default function App() {
  const [activeView, setActiveView] = useState<ActiveView>("cameras");

  const navigation = [
    { id: "cameras", label: "Camera Access", icon: Camera },
    { id: "monitoring", label: "Frame Monitoring", icon: Activity },
    { id: "analysis", label: "Data Analysis", icon: Database },
    { id: "settings", label: "User Settings", icon: User },
  ];

  const renderActiveView = () => {
    switch (activeView) {
      case "cameras":
        return <CameraAccess />;
      case "monitoring":
        return <FrameMonitoring />;
      case "analysis":
        return <DataAnalysis />;
      case "settings":
        return <UserSettings />;
      default:
        return <CameraAccess />;
    }
  };

  return (
    <div className="min-h-screen bg-background">
      {/* Header */}
      <header className="border-b border-border bg-card/50 backdrop-blur">
        <div className="flex h-16 items-center px-6">
          <div className="flex items-center space-x-3">
            <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-primary">
              <Settings className="h-5 w-5 text-primary-foreground" />
            </div>
            <div>
              <h1 className="font-semibold">VisionOps</h1>
              <p className="text-xs text-muted-foreground">Edge Analytics Configuration</p>
            </div>
          </div>
        </div>
      </header>

      <div className="flex">
        {/* Sidebar */}
        <nav className="w-64 border-r border-border bg-card/30 backdrop-blur">
          <div className="p-4">
            <div className="space-y-1">
              {navigation.map((item) => {
                const Icon = item.icon;
                const isActive = activeView === item.id;
                
                return (
                  <Button
                    key={item.id}
                    variant={isActive ? "secondary" : "ghost"}
                    className={`w-full justify-start ${isActive ? 'bg-secondary' : ''}`}
                    onClick={() => setActiveView(item.id as ActiveView)}
                  >
                    <Icon className="h-4 w-4 mr-3" />
                    {item.label}
                  </Button>
                );
              })}
            </div>

            <div className="mt-8">
              <Card className="p-4 bg-card/50">
                <div className="text-sm">
                  <div className="font-medium mb-2">System Info</div>
                  <div className="space-y-1 text-xs text-muted-foreground">
                    <div className="flex justify-between">
                      <span>Version:</span>
                      <span>v2.1.0</span>
                    </div>
                    <div className="flex justify-between">
                      <span>Build:</span>
                      <span>2024.01.15</span>
                    </div>
                    <div className="flex justify-between">
                      <span>Platform:</span>
                      <span>Windows 11</span>
                    </div>
                  </div>
                </div>
              </Card>
            </div>
          </div>
        </nav>

        {/* Main Content */}
        <main className="flex-1 p-6">
          <div className="max-w-6xl mx-auto">
            {renderActiveView()}
          </div>
        </main>
      </div>
    </div>
  );
}