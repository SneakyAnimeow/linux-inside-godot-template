# Bidirectional File Synchronization: Host (Node.js) & Guest (v86 Alpine Linux)

This document outlines the architectural approaches for synchronizing file operations (writes, creates, renames, and deletions) performed inside the emulated guest OS (Alpine Linux) under `/mnt/9p/` back to the host Node.js process.

---

## Option 1: Host-Side v86 Virtual 9p Filesystem Hook (Recommended)

This approach intercepts file changes entirely within the Node.js context by overriding v86's internal 9p filesystem event dispatcher.

### How It Works

1. **Path Cache Initialization:** 
   When the emulator boots, Node.js recursively traverses `emulator.fs9p.inodes` (starting from root inode `0`) to build an initial lookup table of `Map<inodeId, path>`.

2. **NotifyListeners Interception:**
   Override the internal callback hook:
   ```typescript
   const fs9p = (emulator as any).fs9p;
   fs9p.NotifyListeners = (id: number, event: string, metadata?: any) => {
       // Handle events: "newfile", "newdir", "write", "rename", "delete"
   };
   ```

3. **Event Dispatching:**
   * **`write`**: Lookup the path for `id` in the cache, retrieve the updated binary buffer with `await emulator.read_file(path)`, and write it to the host folder.
   * **`newfile` / `newdir`**: Scan the parent directory's entries to resolve the new child name, compute the path, and register it in the cache.
   * **`rename`**: Read `metadata.oldpath` and the new destination, update the cache entries recursively for that directory/file tree.
   * **`delete`**: Lookup the cached path, delete the local host file/folder, and evict the `id` from the cache.

### Pros & Cons

* **Pros:**
  * **Zero Guest Overhead:** No watcher script or background daemon is running in the VM, freeing Alpine CPU cycles.
  * **Low Latency:** Changes sync immediately as soon as they are committed to the virtual device.
  * **No Guest Footprint:** No extra Alpine packages (like networking tools) are needed.
* **Cons:**
  * Uses undocumented v86 properties (`fs9p`, `inodes`, `NotifyListeners`) which could drift across major npm releases.

---

## Option 2: Guest-Side Watcher via Secondary Serial Port (Virtio Console)

This approach runs a standard file watcher daemon inside the guest OS, which writes change events to a dedicated serial port character device.

### How It Works

1. **Configure Second Port:**
   Set `uart1: true` in the `V86Options` configuration inside Node.js.
2. **Alpine Daemon:**
   Run a script (e.g., via `inotifyd`) inside the guest OS watching `/mnt/9p/`.
3. **Payload Stream:**
   When a change is detected, format the event details (event type, relative path, file size, content) and stream it through `/dev/ttyS1`:
   ```bash
   # Conceptual guest command
   inotifyd /path/to/sync_script.sh /mnt/9p:w,d,m
   ```
4. **Node.js Parser:**
   Register a listener for `"serial1-output-byte"` on the host, buffer the stream, parse the incoming frame protocol, and modify files accordingly.

### Pros & Cons

* **Pros:**
  * Uses stable, public v86 APIs (`serial1-output-byte`).
  * Decoupled from internal emulated filesystem representation.
* **Cons:**
  * **Custom Protocol Complexity:** Requires writing a custom binary framer/parser to handle multi-packet transmissions, paths, and contents over a raw serial byte stream.
  * **Guest CPU usage:** Watching the directory and encoding payloads uses VM processing power.

---

## Option 3: Guest-Side Watcher via Local Network Bridge

This approach configures network cards on both the host and guest, establishing a standard HTTP or WebSocket client-server link.

### How It Works

1. **Host Server:** Node.js launches a local HTTP server (or WebSocket server).
2. **Network Bridging:** Configure `net_device` in v86 with a WebSocket proxy relay.
3. **Guest Client:** Alpine initializes a DHCP address, and a script watches `/mnt/9p/`.
4. **HTTP Posts:** When a change occurs, the script pushes the updated file payload back to the host via `curl` or a websocket client.

### Pros & Cons

* **Pros:**
  * Uses standard network communication (no custom serialization layers).
  * Highly robust and decoupled.
* **Cons:**
  * **Resource Intensive:** Emulating network hardware and running TCP/IP networking stacks creates significant emulation overhead.
  * **Slow Boot Time:** Guest OS boot time is delayed while waiting for networking interfaces and DHCP configuration.

---

## Recommendation

We recommend **Option 1 (Host-Side v86 Virtual 9p Filesystem Hook)**.
It provides instant, low-overhead sync without modifying the guest Alpine OS image or running daemon loops inside the guest CPU space.
