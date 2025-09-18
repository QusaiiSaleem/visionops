import { useState } from "react";
import { Database, TestTube2, Eye, EyeOff, CheckCircle, AlertCircle } from "lucide-react";
import { Button } from "./ui/button";
import { Card } from "./ui/card";
import { Input } from "./ui/input";
import { Label } from "./ui/label";
import { Textarea } from "./ui/textarea";
import { Badge } from "./ui/badge";
import { Alert, AlertDescription } from "./ui/alert";

export function SupabaseConfig() {
  const [projectUrl, setProjectUrl] = useState("https://xyzcompany.supabase.co");
  const [anonKey, setAnonKey] = useState("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...");
  const [serviceKey, setServiceKey] = useState("");
  const [showServiceKey, setShowServiceKey] = useState(false);
  const [connectionStatus, setConnectionStatus] = useState<"idle" | "testing" | "success" | "error">("idle");
  const [isTesting, setIsTesting] = useState(false);

  const handleTestConnection = async () => {
    setIsTesting(true);
    setConnectionStatus("testing");
    
    // Simulate API test
    await new Promise(resolve => setTimeout(resolve, 2000));
    
    // Simulate successful connection
    setConnectionStatus("success");
    setIsTesting(false);
  };

  const handleSave = async () => {
    // Simulate saving configuration
    await new Promise(resolve => setTimeout(resolve, 1000));
    alert("Configuration saved successfully!");
  };

  const getStatusIcon = () => {
    switch (connectionStatus) {
      case "success":
        return <CheckCircle className="h-4 w-4 text-green-500" />;
      case "error":
        return <AlertCircle className="h-4 w-4 text-red-500" />;
      default:
        return <Database className="h-4 w-4 text-muted-foreground" />;
    }
  };

  const getStatusBadge = () => {
    switch (connectionStatus) {
      case "testing":
        return <Badge variant="secondary">Testing...</Badge>;
      case "success":
        return <Badge variant="default">Connected</Badge>;
      case "error":
        return <Badge variant="destructive">Failed</Badge>;
      default:
        return <Badge variant="outline">Not Tested</Badge>;
    }
  };

  return (
    <div className="space-y-6">
      <div>
        <h1>Supabase Configuration</h1>
        <p className="text-muted-foreground mt-1">
          Configure cloud database connection for syncing analytics data
        </p>
      </div>

      <Alert>
        <Database className="h-4 w-4" />
        <AlertDescription>
          Only metadata and analytics insights are synchronized to the cloud. 
          Video streams remain local for privacy and bandwidth efficiency.
        </AlertDescription>
      </Alert>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <Card className="p-6">
          <div className="space-y-4">
            <div className="flex items-center justify-between mb-4">
              <Label>Connection Settings</Label>
              {getStatusBadge()}
            </div>

            <div>
              <Label htmlFor="project-url">Project URL</Label>
              <Input
                id="project-url"
                value={projectUrl}
                onChange={(e) => setProjectUrl(e.target.value)}
                placeholder="https://your-project.supabase.co"
                className="mt-1"
              />
              <p className="text-xs text-muted-foreground mt-1">
                Found in your Supabase project settings
              </p>
            </div>

            <div>
              <Label htmlFor="anon-key">Anon/Public Key</Label>
              <Textarea
                id="anon-key"
                value={anonKey}
                onChange={(e) => setAnonKey(e.target.value)}
                placeholder="eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
                className="mt-1 h-20 resize-none"
              />
              <p className="text-xs text-muted-foreground mt-1">
                Safe to expose, used for anonymous operations
              </p>
            </div>

            <div>
              <Label htmlFor="service-key">Service Role Key (Optional)</Label>
              <div className="relative">
                <Textarea
                  id="service-key"
                  type={showServiceKey ? "text" : "password"}
                  value={serviceKey}
                  onChange={(e) => setServiceKey(e.target.value)}
                  placeholder="eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
                  className="mt-1 h-20 resize-none pr-10"
                />
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  className="absolute top-2 right-2 h-6 w-6 p-0"
                  onClick={() => setShowServiceKey(!showServiceKey)}
                >
                  {showServiceKey ? (
                    <EyeOff className="h-3 w-3" />
                  ) : (
                    <Eye className="h-3 w-3" />
                  )}
                </Button>
              </div>
              <p className="text-xs text-muted-foreground mt-1">
                Required for advanced features, keep secure
              </p>
            </div>

            <div className="flex gap-2 pt-4">
              <Button 
                onClick={handleTestConnection}
                disabled={isTesting}
                variant="outline"
                className="flex-1"
              >
                <TestTube2 className={`h-4 w-4 mr-2 ${isTesting ? 'animate-spin' : ''}`} />
                Test Connection
              </Button>
              <Button 
                onClick={handleSave}
                className="flex-1"
                disabled={connectionStatus !== "success"}
              >
                Save Configuration
              </Button>
            </div>
          </div>
        </Card>

        <Card className="p-6">
          <div className="space-y-4">
            <div className="flex items-center space-x-2 mb-4">
              {getStatusIcon()}
              <Label>Connection Status</Label>
            </div>

            {connectionStatus === "idle" && (
              <div className="text-center py-8">
                <Database className="h-12 w-12 text-muted-foreground mx-auto mb-4 opacity-50" />
                <p className="text-muted-foreground">
                  Configure your Supabase credentials and test the connection
                </p>
              </div>
            )}

            {connectionStatus === "testing" && (
              <div className="text-center py-8">
                <div className="animate-spin h-8 w-8 border-2 border-primary border-t-transparent rounded-full mx-auto mb-4"></div>
                <p className="text-muted-foreground">Testing connection...</p>
              </div>
            )}

            {connectionStatus === "success" && (
              <div className="space-y-4">
                <div className="text-center py-4">
                  <CheckCircle className="h-12 w-12 text-green-500 mx-auto mb-4" />
                  <p className="font-medium text-green-600">Connection Successful!</p>
                  <p className="text-sm text-muted-foreground">Ready to sync data</p>
                </div>

                <div className="grid grid-cols-2 gap-4 pt-4 border-t">
                  <div className="text-center">
                    <div className="text-lg font-semibold">156ms</div>
                    <div className="text-xs text-muted-foreground">Latency</div>
                  </div>
                  <div className="text-center">
                    <div className="text-lg font-semibold">Active</div>
                    <div className="text-xs text-muted-foreground">Status</div>
                  </div>
                </div>

                <div className="space-y-2">
                  <Label>Sync Settings</Label>
                  <div className="space-y-1 text-sm text-muted-foreground">
                    <div className="flex justify-between">
                      <span>Analytics Data:</span>
                      <span>Every 5 minutes</span>
                    </div>
                    <div className="flex justify-between">
                      <span>System Status:</span>
                      <span>Real-time</span>
                    </div>
                    <div className="flex justify-between">
                      <span>Configuration:</span>
                      <span>On change</span>
                    </div>
                  </div>
                </div>
              </div>
            )}

            {connectionStatus === "error" && (
              <div className="text-center py-8">
                <AlertCircle className="h-12 w-12 text-red-500 mx-auto mb-4" />
                <p className="font-medium text-red-600">Connection Failed</p>
                <p className="text-sm text-muted-foreground">
                  Please check your credentials and try again
                </p>
              </div>
            )}
          </div>
        </Card>
      </div>
    </div>
  );
}