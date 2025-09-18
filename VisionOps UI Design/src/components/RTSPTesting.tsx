import { useState } from "react";
import { Play, Square, TestTube2, CheckCircle, XCircle, AlertCircle } from "lucide-react";
import { Button } from "./ui/button";
import { Card } from "./ui/card";
import { Input } from "./ui/input";
import { Label } from "./ui/label";
import { Badge } from "./ui/badge";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "./ui/select";

interface TestResult {
  status: "success" | "error" | "warning";
  message: string;
  timestamp: Date;
}

export function RTSPTesting() {
  const [rtspUrl, setRtspUrl] = useState("rtsp://192.168.1.101:554/stream1");
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [isTesting, setIsTesting] = useState(false);
  const [isStreaming, setIsStreaming] = useState(false);
  const [testResults, setTestResults] = useState<TestResult[]>([]);

  const handleTest = async () => {
    setIsTesting(true);
    
    // Simulate RTSP connection test
    const tests = [
      { message: "Resolving hostname...", delay: 500 },
      { message: "Establishing TCP connection...", delay: 800 },
      { message: "RTSP handshake...", delay: 1000 },
      { message: "Negotiating stream format...", delay: 700 },
      { message: "Connection successful", delay: 500 },
    ];

    const results: TestResult[] = [];
    
    for (const test of tests) {
      await new Promise(resolve => setTimeout(resolve, test.delay));
      results.push({
        status: test.message === "Connection successful" ? "success" : "success",
        message: test.message,
        timestamp: new Date()
      });
      setTestResults([...results]);
    }
    
    setIsTesting(false);
  };

  const handleStartStream = async () => {
    setIsStreaming(true);
    // Simulate starting stream
    await new Promise(resolve => setTimeout(resolve, 1000));
  };

  const handleStopStream = () => {
    setIsStreaming(false);
  };

  const getStatusIcon = (status: TestResult["status"]) => {
    switch (status) {
      case "success":
        return <CheckCircle className="h-4 w-4 text-green-500" />;
      case "error":
        return <XCircle className="h-4 w-4 text-red-500" />;
      case "warning":
        return <AlertCircle className="h-4 w-4 text-yellow-500" />;
    }
  };

  return (
    <div className="space-y-6">
      <div>
        <h1>RTSP Connection Testing</h1>
        <p className="text-muted-foreground mt-1">
          Test and validate RTSP stream connections
        </p>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <Card className="p-6">
          <div className="space-y-4">
            <div>
              <Label htmlFor="rtsp-url">RTSP URL</Label>
              <Input
                id="rtsp-url"
                value={rtspUrl}
                onChange={(e) => setRtspUrl(e.target.value)}
                placeholder="rtsp://192.168.1.100:554/stream1"
                className="mt-1"
              />
            </div>

            <div className="grid grid-cols-2 gap-4">
              <div>
                <Label htmlFor="username">Username</Label>
                <Input
                  id="username"
                  value={username}
                  onChange={(e) => setUsername(e.target.value)}
                  placeholder="admin"
                  className="mt-1"
                />
              </div>
              <div>
                <Label htmlFor="password">Password</Label>
                <Input
                  id="password"
                  type="password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  placeholder="password"
                  className="mt-1"
                />
              </div>
            </div>

            <div>
              <Label htmlFor="resolution">Stream Quality</Label>
              <Select>
                <SelectTrigger className="mt-1">
                  <SelectValue placeholder="Select quality" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="high">High (1080p)</SelectItem>
                  <SelectItem value="medium">Medium (720p)</SelectItem>
                  <SelectItem value="low">Low (480p)</SelectItem>
                </SelectContent>
              </Select>
            </div>

            <div className="flex gap-2 pt-2">
              <Button 
                onClick={handleTest} 
                disabled={isTesting || isStreaming}
                className="flex-1"
              >
                <TestTube2 className={`h-4 w-4 mr-2 ${isTesting ? 'animate-pulse' : ''}`} />
                {isTesting ? "Testing..." : "Test Connection"}
              </Button>
              
              {!isStreaming ? (
                <Button 
                  onClick={handleStartStream}
                  variant="outline"
                  className="flex-1"
                >
                  <Play className="h-4 w-4 mr-2" />
                  Start Preview
                </Button>
              ) : (
                <Button 
                  onClick={handleStopStream}
                  variant="outline"
                  className="flex-1"
                >
                  <Square className="h-4 w-4 mr-2" />
                  Stop Preview
                </Button>
              )}
            </div>
          </div>
        </Card>

        <Card className="p-6">
          <div className="space-y-4">
            <div className="flex items-center justify-between">
              <Label>Stream Preview</Label>
              {isStreaming && (
                <Badge variant="default">
                  Live
                </Badge>
              )}
            </div>
            
            <div className="aspect-video bg-muted rounded-lg flex items-center justify-center">
              {isStreaming ? (
                <div className="text-center">
                  <div className="w-16 h-16 bg-primary/10 rounded-full flex items-center justify-center mx-auto mb-2">
                    <Play className="h-8 w-8 text-primary" />
                  </div>
                  <p className="text-sm text-muted-foreground">Live Stream Active</p>
                  <p className="text-xs text-muted-foreground">1920x1080 @ 25fps</p>
                </div>
              ) : (
                <div className="text-center text-muted-foreground">
                  <div className="w-16 h-16 bg-muted-foreground/10 rounded-full flex items-center justify-center mx-auto mb-2">
                    <Play className="h-8 w-8" />
                  </div>
                  <p className="text-sm">No stream active</p>
                </div>
              )}
            </div>

            {testResults.length > 0 && (
              <div className="space-y-2">
                <Label>Test Results</Label>
                <div className="space-y-2 max-h-32 overflow-y-auto">
                  {testResults.map((result, index) => (
                    <div key={index} className="flex items-center space-x-2 text-sm">
                      {getStatusIcon(result.status)}
                      <span className="flex-1">{result.message}</span>
                      <span className="text-xs text-muted-foreground">
                        {result.timestamp.toLocaleTimeString()}
                      </span>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </div>
        </Card>
      </div>
    </div>
  );
}