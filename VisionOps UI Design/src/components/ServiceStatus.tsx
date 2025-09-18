import { CheckCircle, XCircle, Play, Square, RefreshCw } from "lucide-react";
import { Button } from "./ui/button";
import { Card } from "./ui/card";
import { Badge } from "./ui/badge";
import { useState } from "react";

export function ServiceStatus() {
  const [isServiceRunning, setIsServiceRunning] = useState(true);
  const [isLoading, setIsLoading] = useState(false);

  const handleServiceToggle = async () => {
    setIsLoading(true);
    // Simulate API call
    await new Promise(resolve => setTimeout(resolve, 1500));
    setIsServiceRunning(!isServiceRunning);
    setIsLoading(false);
  };

  const handleRefresh = async () => {
    setIsLoading(true);
    await new Promise(resolve => setTimeout(resolve, 1000));
    setIsLoading(false);
  };

  return (
    <div className="space-y-6">
      <div>
        <h1>Service Status</h1>
        <p className="text-muted-foreground mt-1">
          Monitor and control the VisionOps background service
        </p>
      </div>

      <Card className="p-6">
        <div className="flex items-center justify-between">
          <div className="flex items-center space-x-4">
            <div className="flex items-center space-x-2">
              {isServiceRunning ? (
                <CheckCircle className="h-5 w-5 text-green-500" />
              ) : (
                <XCircle className="h-5 w-5 text-red-500" />
              )}
              <span className="font-medium">VisionOps.Service</span>
            </div>
            <Badge variant={isServiceRunning ? "default" : "destructive"}>
              {isServiceRunning ? "Running" : "Stopped"}
            </Badge>
          </div>

          <div className="flex items-center space-x-2">
            <Button 
              variant="outline" 
              size="sm" 
              onClick={handleRefresh}
              disabled={isLoading}
            >
              <RefreshCw className={`h-4 w-4 ${isLoading ? 'animate-spin' : ''}`} />
            </Button>
            <Button 
              onClick={handleServiceToggle}
              disabled={isLoading}
              variant={isServiceRunning ? "destructive" : "default"}
            >
              {isLoading ? (
                <RefreshCw className="h-4 w-4 animate-spin mr-2" />
              ) : isServiceRunning ? (
                <Square className="h-4 w-4 mr-2" />
              ) : (
                <Play className="h-4 w-4 mr-2" />
              )}
              {isServiceRunning ? "Stop Service" : "Start Service"}
            </Button>
          </div>
        </div>

        {isServiceRunning && (
          <div className="mt-6 grid grid-cols-3 gap-4 pt-6 border-t border-border">
            <div className="text-center">
              <div className="text-2xl font-semibold">3</div>
              <div className="text-sm text-muted-foreground">Cameras Active</div>
            </div>
            <div className="text-center">
              <div className="text-2xl font-semibold">24/7</div>
              <div className="text-sm text-muted-foreground">Uptime</div>
            </div>
            <div className="text-center">
              <div className="text-2xl font-semibold">Normal</div>
              <div className="text-sm text-muted-foreground">System Health</div>
            </div>
          </div>
        )}
      </Card>
    </div>
  );
}