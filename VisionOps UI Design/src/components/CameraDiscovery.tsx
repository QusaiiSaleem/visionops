import { useState } from "react";
import { Search, Camera, Plus, Settings2, Trash2 } from "lucide-react";
import { Button } from "./ui/button";
import { Card } from "./ui/card";
import { Input } from "./ui/input";
import { Badge } from "./ui/badge";
import { Label } from "./ui/label";

interface Camera {
  id: string;
  name: string;
  ip: string;
  status: "online" | "offline" | "testing";
  rtspUrl: string;
}

export function CameraDiscovery() {
  const [cameras, setCameras] = useState<Camera[]>([
    { id: "1", name: "Front Entrance", ip: "192.168.1.101", status: "online", rtspUrl: "rtsp://192.168.1.101:554/stream1" },
    { id: "2", name: "Loading Dock", ip: "192.168.1.102", status: "online", rtspUrl: "rtsp://192.168.1.102:554/stream1" },
    { id: "3", name: "Office Area", ip: "192.168.1.103", status: "offline", rtspUrl: "rtsp://192.168.1.103:554/stream1" },
  ]);
  
  const [isScanning, setIsScanning] = useState(false);
  const [manualIp, setManualIp] = useState("");

  const handleScan = async () => {
    setIsScanning(true);
    // Simulate network scan
    await new Promise(resolve => setTimeout(resolve, 3000));
    setIsScanning(false);
  };

  const handleAddManual = () => {
    if (manualIp) {
      const newCamera: Camera = {
        id: Date.now().toString(),
        name: `Camera ${cameras.length + 1}`,
        ip: manualIp,
        status: "testing",
        rtspUrl: `rtsp://${manualIp}:554/stream1`
      };
      setCameras([...cameras, newCamera]);
      setManualIp("");
    }
  };

  const handleRemoveCamera = (id: string) => {
    setCameras(cameras.filter(camera => camera.id !== id));
  };

  return (
    <div className="space-y-6">
      <div>
        <h1>Camera Discovery</h1>
        <p className="text-muted-foreground mt-1">
          Discover and configure cameras on your network
        </p>
      </div>

      <Card className="p-6">
        <div className="flex flex-col sm:flex-row gap-4 mb-6">
          <Button 
            onClick={handleScan} 
            disabled={isScanning}
            className="flex-1"
          >
            <Search className={`h-4 w-4 mr-2 ${isScanning ? 'animate-spin' : ''}`} />
            {isScanning ? "Scanning Network..." : "Auto-Discover Cameras"}
          </Button>
          
          <div className="flex gap-2 flex-1">
            <Input
              placeholder="192.168.1.100"
              value={manualIp}
              onChange={(e) => setManualIp(e.target.value)}
              className="flex-1"
            />
            <Button onClick={handleAddManual} variant="outline">
              <Plus className="h-4 w-4" />
            </Button>
          </div>
        </div>

        <div className="space-y-3">
          <Label>Discovered Cameras ({cameras.length})</Label>
          {cameras.length === 0 ? (
            <div className="text-center py-8 text-muted-foreground">
              <Camera className="h-8 w-8 mx-auto mb-2 opacity-50" />
              <p>No cameras found. Try scanning or add manually.</p>
            </div>
          ) : (
            <div className="space-y-3">
              {cameras.map((camera) => (
                <Card key={camera.id} className="p-4">
                  <div className="flex items-center justify-between">
                    <div className="flex items-center space-x-4">
                      <Camera className="h-5 w-5 text-muted-foreground" />
                      <div>
                        <div className="flex items-center space-x-2">
                          <span className="font-medium">{camera.name}</span>
                          <Badge 
                            variant={camera.status === "online" ? "default" : 
                                   camera.status === "testing" ? "secondary" : "destructive"}
                          >
                            {camera.status}
                          </Badge>
                        </div>
                        <div className="text-sm text-muted-foreground">
                          {camera.ip} • {camera.rtspUrl}
                        </div>
                      </div>
                    </div>
                    
                    <div className="flex items-center space-x-2">
                      <Button variant="outline" size="sm">
                        <Settings2 className="h-4 w-4" />
                      </Button>
                      <Button 
                        variant="outline" 
                        size="sm"
                        onClick={() => handleRemoveCamera(camera.id)}
                      >
                        <Trash2 className="h-4 w-4" />
                      </Button>
                    </div>
                  </div>
                </Card>
              ))}
            </div>
          )}
        </div>
      </Card>
    </div>
  );
}