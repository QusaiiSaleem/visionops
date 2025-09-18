import { useState } from "react";
import { User, Building, MapPin, Save, Key, Shield } from "lucide-react";
import { Button } from "./ui/button";
import { Card } from "./ui/card";
import { Input } from "./ui/input";
import { Label } from "./ui/label";
import { Textarea } from "./ui/textarea";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "./ui/select";
import { Separator } from "./ui/separator";
import { Badge } from "./ui/badge";

export function UserSettings() {
  const [userConfig, setUserConfig] = useState({
    organizationName: "Acme Corporation",
    locationName: "Main Office - Building A",
    contactEmail: "admin@acme.com",
    timezone: "America/New_York",
    userId: "USR-2024-001",
    deploymentId: "DEPLOY-MAIN-001",
    description: "Primary VisionOps deployment for main office building entrance monitoring and analytics."
  });

  const [systemConfig, setSystemConfig] = useState({
    dataRetention: "30",
    syncInterval: "5",
    privacyMode: true,
    anonymizeData: true
  });

  const [isSaving, setIsSaving] = useState(false);

  const timezones = [
    { value: "America/New_York", label: "Eastern Time (ET)" },
    { value: "America/Chicago", label: "Central Time (CT)" },
    { value: "America/Denver", label: "Mountain Time (MT)" },
    { value: "America/Los_Angeles", label: "Pacific Time (PT)" },
    { value: "Europe/London", label: "GMT/BST" },
    { value: "Europe/Paris", label: "Central European Time" },
    { value: "Asia/Tokyo", label: "Japan Standard Time" },
    { value: "Australia/Sydney", label: "Australian Eastern Time" }
  ];

  const handleSave = async () => {
    setIsSaving(true);
    // Simulate save operation
    await new Promise(resolve => setTimeout(resolve, 1500));
    setIsSaving(false);
  };

  const generateNewId = (type: "user" | "deployment") => {
    const prefix = type === "user" ? "USR" : "DEPLOY";
    const year = new Date().getFullYear();
    const random = Math.floor(Math.random() * 1000).toString().padStart(3, "0");
    const newId = `${prefix}-${year}-${random}`;
    
    if (type === "user") {
      setUserConfig({ ...userConfig, userId: newId });
    } else {
      setUserConfig({ ...userConfig, deploymentId: newId });
    }
  };

  return (
    <div className="space-y-6">
      <div>
        <h1>User Settings</h1>
        <p className="text-muted-foreground mt-1">
          Configure user identification and system preferences
        </p>
      </div>

      {/* User Identification */}
      <Card className="p-6">
        <div className="space-y-4">
          <div className="flex items-center gap-2 mb-4">
            <User className="h-5 w-5" />
            <Label className="text-base">User Identification</Label>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <div>
              <Label htmlFor="org-name">Organization Name</Label>
              <Input
                id="org-name"
                value={userConfig.organizationName}
                onChange={(e) => setUserConfig({ ...userConfig, organizationName: e.target.value })}
                className="mt-1"
              />
            </div>
            <div>
              <Label htmlFor="location">Location Name</Label>
              <Input
                id="location"
                value={userConfig.locationName}
                onChange={(e) => setUserConfig({ ...userConfig, locationName: e.target.value })}
                className="mt-1"
              />
            </div>
            <div>
              <Label htmlFor="email">Contact Email</Label>
              <Input
                id="email"
                type="email"
                value={userConfig.contactEmail}
                onChange={(e) => setUserConfig({ ...userConfig, contactEmail: e.target.value })}
                className="mt-1"
              />
            </div>
            <div>
              <Label htmlFor="timezone">Timezone</Label>
              <Select value={userConfig.timezone} onValueChange={(value) => setUserConfig({ ...userConfig, timezone: value })}>
                <SelectTrigger className="mt-1">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {timezones.map(tz => (
                    <SelectItem key={tz.value} value={tz.value}>
                      {tz.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>

          <div>
            <Label htmlFor="description">Deployment Description</Label>
            <Textarea
              id="description"
              value={userConfig.description}
              onChange={(e) => setUserConfig({ ...userConfig, description: e.target.value })}
              className="mt-1"
              rows={3}
            />
          </div>
        </div>
      </Card>

      {/* System Identifiers */}
      <Card className="p-6">
        <div className="space-y-4">
          <div className="flex items-center gap-2 mb-4">
            <Key className="h-5 w-5" />
            <Label className="text-base">System Identifiers</Label>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <div>
              <Label htmlFor="user-id">User ID</Label>
              <div className="flex gap-2 mt-1">
                <Input
                  id="user-id"
                  value={userConfig.userId}
                  onChange={(e) => setUserConfig({ ...userConfig, userId: e.target.value })}
                  className="font-mono"
                />
                <Button 
                  variant="outline" 
                  onClick={() => generateNewId("user")}
                  className="px-3"
                >
                  Generate
                </Button>
              </div>
              <p className="text-xs text-muted-foreground mt-1">
                Unique identifier for this user/organization
              </p>
            </div>
            <div>
              <Label htmlFor="deployment-id">Deployment ID</Label>
              <div className="flex gap-2 mt-1">
                <Input
                  id="deployment-id"
                  value={userConfig.deploymentId}
                  onChange={(e) => setUserConfig({ ...userConfig, deploymentId: e.target.value })}
                  className="font-mono"
                />
                <Button 
                  variant="outline" 
                  onClick={() => generateNewId("deployment")}
                  className="px-3"
                >
                  Generate
                </Button>
              </div>
              <p className="text-xs text-muted-foreground mt-1">
                Unique identifier for this VisionOps installation
              </p>
            </div>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-3 gap-4 pt-4 border-t">
            <div className="text-center">
              <div className="text-sm text-muted-foreground">System Version</div>
              <Badge variant="outline" className="mt-1">v2.1.0</Badge>
            </div>
            <div className="text-center">
              <div className="text-sm text-muted-foreground">Installation Date</div>
              <Badge variant="outline" className="mt-1">2024-01-15</Badge>
            </div>
            <div className="text-center">
              <div className="text-sm text-muted-foreground">License Type</div>
              <Badge variant="default" className="mt-1">Commercial</Badge>
            </div>
          </div>
        </div>
      </Card>

      {/* Privacy & Data Settings */}
      <Card className="p-6">
        <div className="space-y-4">
          <div className="flex items-center gap-2 mb-4">
            <Shield className="h-5 w-5" />
            <Label className="text-base">Privacy & Data Management</Label>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            <div>
              <Label htmlFor="retention">Data Retention (days)</Label>
              <Select value={systemConfig.dataRetention} onValueChange={(value) => setSystemConfig({ ...systemConfig, dataRetention: value })}>
                <SelectTrigger className="mt-1">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="7">7 days</SelectItem>
                  <SelectItem value="30">30 days</SelectItem>
                  <SelectItem value="90">90 days</SelectItem>
                  <SelectItem value="365">1 year</SelectItem>
                </SelectContent>
              </Select>
              <p className="text-xs text-muted-foreground mt-1">
                How long to keep analytics data before automatic deletion
              </p>
            </div>

            <div>
              <Label htmlFor="sync-interval">Cloud Sync Interval (minutes)</Label>
              <Select value={systemConfig.syncInterval} onValueChange={(value) => setSystemConfig({ ...systemConfig, syncInterval: value })}>
                <SelectTrigger className="mt-1">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="1">Every minute</SelectItem>
                  <SelectItem value="5">Every 5 minutes</SelectItem>
                  <SelectItem value="15">Every 15 minutes</SelectItem>
                  <SelectItem value="60">Every hour</SelectItem>
                </SelectContent>
              </Select>
              <p className="text-xs text-muted-foreground mt-1">
                How frequently to sync analytics data to the cloud
              </p>
            </div>
          </div>

          <Separator />

          <div className="space-y-3">
            <div className="flex items-center justify-between">
              <div>
                <div className="font-medium">Privacy Mode</div>
                <div className="text-sm text-muted-foreground">
                  Video streams never leave the local device
                </div>
              </div>
              <Badge variant="default">Always Enabled</Badge>
            </div>

            <div className="flex items-center justify-between">
              <div>
                <div className="font-medium">Data Anonymization</div>
                <div className="text-sm text-muted-foreground">
                  Remove personally identifiable information from analytics
                </div>
              </div>
              <Badge variant="default">Always Enabled</Badge>
            </div>

            <div className="flex items-center justify-between">
              <div>
                <div className="font-medium">Local Processing</div>
                <div className="text-sm text-muted-foreground">
                  All AI inference happens on this device
                </div>
              </div>
              <Badge variant="default">Always Enabled</Badge>
            </div>
          </div>
        </div>
      </Card>

      {/* Save Button */}
      <div className="flex justify-end">
        <Button onClick={handleSave} disabled={isSaving}>
          <Save className={`h-4 w-4 mr-2 ${isSaving ? 'animate-spin' : ''}`} />
          {isSaving ? "Saving..." : "Save Settings"}
        </Button>
      </div>
    </div>
  );
}