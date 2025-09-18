import { useState } from "react";
import { Camera, Plus, Search, Settings2, Trash2, Power, WifiOff } from "lucide-react";
import { Button } from "./ui/button";
import { Card } from "./ui/card";
import { Input } from "./ui/input";
import { Label } from "./ui/label";
import { Badge } from "./ui/badge";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "./ui/table";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogTrigger } from "./ui/dialog";

interface CameraDevice {
  id: string;
  name: string;
  ipAddress: string;
  rtspUrl: string;
  status: "online" | "offline" | "connecting";
  brand: string;
  resolution: string;
  fps: number;
}

export function CameraAccess() {
  const [cameras, setCameras] = useState<CameraDevice[]>([
    {
      id: "1",
      name: "Front Entrance",
      ipAddress: "192.168.1.101",
      rtspUrl: "rtsp://192.168.1.101:554/stream1",
      status: "online",
      brand: "Hikvision",
      resolution: "1920x1080",
      fps: 25
    },
    {
      id: "2", 
      name: "Loading Dock",
      ipAddress: "192.168.1.102",
      rtspUrl: "rtsp://192.168.1.102:554/stream1",
      status: "online",
      brand: "Dahua",
      resolution: "1920x1080",
      fps: 30
    },
    {
      id: "3",
      name: "Office Area",
      ipAddress: "192.168.1.103", 
      rtspUrl: "rtsp://192.168.1.103:554/stream1",
      status: "offline",
      brand: "Axis",
      resolution: "1280x720",
      fps: 15
    }
  ]);

  const [isScanning, setIsScanning] = useState(false);
  const [newCamera, setNewCamera] = useState({
    name: "",
    ipAddress: "",
    rtspUrl: "",
    username: "",
    password: ""
  });

  const handleAutoDiscover = async () => {
    setIsScanning(true);
    // Simulate network scanning
    await new Promise(resolve => setTimeout(resolve, 3000));
    setIsScanning(false);
    
    // Add discovered camera
    const discoveredCamera: CameraDevice = {
      id: Date.now().toString(),
      name: "Discovered Camera",
      ipAddress: "192.168.1.104",
      rtspUrl: "rtsp://192.168.1.104:554/stream1",
      status: "connecting",
      brand: "Generic",
      resolution: "1920x1080",
      fps: 25
    };
    setCameras([...cameras, discoveredCamera]);
  };

  const handleAddCamera = () => {
    if (newCamera.name && newCamera.ipAddress && newCamera.rtspUrl) {
      const camera: CameraDevice = {
        id: Date.now().toString(),
        name: newCamera.name,
        ipAddress: newCamera.ipAddress,
        rtspUrl: newCamera.rtspUrl,
        status: "connecting",
        brand: "Manual",
        resolution: "1920x1080",
        fps: 25
      };
      setCameras([...cameras, camera]);
      setNewCamera({ name: "", ipAddress: "", rtspUrl: "", username: "", password: "" });
    }
  };

  const handleRemoveCamera = (id: string) => {
    setCameras(cameras.filter(camera => camera.id !== id));
  };

  const getStatusBadge = (status: CameraDevice["status"]) => {
    switch (status) {
      case "online":
        return <Badge variant="default">Online</Badge>;
      case "offline":
        return <Badge variant="destructive">Offline</Badge>;
      case "connecting":
        return <Badge variant="secondary">Connecting</Badge>;
      default:
        return <Badge variant="outline">Unknown</Badge>;
    }
  };

  const getStatusIcon = (status: CameraDevice["status"]) => {
    switch (status) {
      case "online":
        return <Power className="h-4 w-4 text-green-500" />;
      case "offline":
        return <WifiOff className="h-4 w-4 text-red-500" />;
      case "connecting":
        return <Power className="h-4 w-4 text-yellow-500" />;
      default:
        return <WifiOff className="h-4 w-4 text-gray-500" />;
    }
  };

  return (
    <div className="space-y-6">
      <div>
        <h1>Camera Access</h1>
        <p className="text-muted-foreground mt-1">
          Manage camera connections and configure network access
        </p>
      </div>

      {/* Action Buttons */}
      <div className="flex gap-4">
        <Button 
          onClick={handleAutoDiscover}
          disabled={isScanning}
          className="flex-1"
        >
          <Search className={`h-4 w-4 mr-2 ${isScanning ? 'animate-spin' : ''}`} />
          {isScanning ? "Scanning Network..." : "Auto-Discover Cameras"}
        </Button>
        
        <Dialog>
          <DialogTrigger asChild>
            <Button variant="outline" className="flex-1">
              <Plus className="h-4 w-4 mr-2" />
              Add Camera Manually
            </Button>
          </DialogTrigger>
          <DialogContent className="sm:max-w-md">
            <DialogHeader>
              <DialogTitle>Add New Camera</DialogTitle>
            </DialogHeader>
            <div className="space-y-4">
              <div>
                <Label htmlFor="camera-name">Camera Name</Label>
                <Input
                  id="camera-name"
                  value={newCamera.name}
                  onChange={(e) => setNewCamera({...newCamera, name: e.target.value})}
                  placeholder="e.g. Front Entrance"
                  className="mt-1"
                />
              </div>
              <div>
                <Label htmlFor="ip-address">IP Address</Label>
                <Input
                  id="ip-address"
                  value={newCamera.ipAddress}
                  onChange={(e) => setNewCamera({...newCamera, ipAddress: e.target.value})}
                  placeholder="192.168.1.100"
                  className="mt-1"
                />
              </div>
              <div>
                <Label htmlFor="rtsp-url">RTSP URL</Label>
                <Input
                  id="rtsp-url"
                  value={newCamera.rtspUrl}
                  onChange={(e) => setNewCamera({...newCamera, rtspUrl: e.target.value})}
                  placeholder="rtsp://192.168.1.100:554/stream1"
                  className="mt-1"
                />
              </div>
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <Label htmlFor="username">Username</Label>
                  <Input
                    id="username"
                    value={newCamera.username}
                    onChange={(e) => setNewCamera({...newCamera, username: e.target.value})}
                    placeholder="admin"
                    className="mt-1"
                  />
                </div>
                <div>
                  <Label htmlFor="password">Password</Label>
                  <Input
                    id="password"
                    type="password"
                    value={newCamera.password}
                    onChange={(e) => setNewCamera({...newCamera, password: e.target.value})}
                    placeholder="password"
                    className="mt-1"
                  />
                </div>
              </div>
              <Button onClick={handleAddCamera} className="w-full">
                Add Camera
              </Button>
            </div>
          </DialogContent>
        </Dialog>
      </div>

      {/* Camera List */}
      <Card>
        <div className="p-6">
          <div className="flex items-center justify-between mb-4">
            <Label>Configured Cameras ({cameras.length})</Label>
            <div className="flex gap-2 text-sm text-muted-foreground">
              <span className="flex items-center gap-1">
                <div className="w-2 h-2 bg-green-500 rounded-full"></div>
                {cameras.filter(c => c.status === 'online').length} Online
              </span>
              <span className="flex items-center gap-1">
                <div className="w-2 h-2 bg-red-500 rounded-full"></div>
                {cameras.filter(c => c.status === 'offline').length} Offline
              </span>
            </div>
          </div>

          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Status</TableHead>
                <TableHead>Camera</TableHead>
                <TableHead>IP Address</TableHead>
                <TableHead>Brand</TableHead>
                <TableHead>Resolution</TableHead>
                <TableHead>FPS</TableHead>
                <TableHead className="text-right">Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {cameras.map((camera) => (
                <TableRow key={camera.id}>
                  <TableCell>
                    <div className="flex items-center gap-2">
                      {getStatusIcon(camera.status)}
                      {getStatusBadge(camera.status)}
                    </div>
                  </TableCell>
                  <TableCell>
                    <div>
                      <div className="font-medium">{camera.name}</div>
                      <div className="text-sm text-muted-foreground">{camera.rtspUrl}</div>
                    </div>
                  </TableCell>
                  <TableCell>{camera.ipAddress}</TableCell>
                  <TableCell>{camera.brand}</TableCell>
                  <TableCell>{camera.resolution}</TableCell>
                  <TableCell>{camera.fps}</TableCell>
                  <TableCell className="text-right">
                    <div className="flex items-center justify-end gap-2">
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
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>

          {cameras.length === 0 && (
            <div className="text-center py-8">
              <Camera className="h-12 w-12 text-muted-foreground mx-auto mb-4 opacity-50" />
              <p className="text-muted-foreground">No cameras configured</p>
              <p className="text-sm text-muted-foreground">Use auto-discover or add manually</p>
            </div>
          )}
        </div>
      </Card>
    </div>
  );
}