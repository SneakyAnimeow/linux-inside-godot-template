import fs from "node:fs";
import url from "node:url";
import path from "node:path";
import {V86} from "v86";

export const createVM = async (onOutput: (c: string) => void) => {
    const _urlDir = url.fileURLToPath(new URL(".", import.meta.url));
    const baseDir = path.basename(_urlDir) === "dist" ? path.join(_urlDir, "..") : _urlDir;

    console.log("Now booting Alpine, please stand by ...");

    const emulator = new V86({
        bios: {url: path.join(baseDir, "bios/seabios.bin")},
        vga_bios: {url: path.join(baseDir, "bios/vgabios.bin")},
        bzimage: {url: path.join(baseDir, "images/boot/vmlinuz-virt")},
        initrd: {url: path.join(baseDir, "images/boot/initramfs-virt")},
        cdrom: {url: path.join(baseDir, "images/alpine.iso")},
        filesystem: {},
        cmdline: "rw console=ttyS0,115200",
        autostart: true,
        wasm_path: path.join(baseDir, "v86.wasm"),
        memory_size: 128 * 1024 * 1024,
        // net_device: {
        //     type: "virtio",
        //     relay_url: "wss://wisp.mercurywork.shop"
        // }
    });

    let booted = false;
    let boot_buffer = "";

    emulator.add_listener("serial0-output-byte", (byte: number) => {
        const c = String.fromCharCode(byte);
        if (onOutput) onOutput(c);

        boot_buffer += c;
        if (!booted && boot_buffer.includes("localhost login:")) {
            booted = true;

            type FSONestedType = {
                name: string,
                content: string | null,
                children?: FSONestedType[] | null
            };

            class FSO {
                name: string;
                content: string | null;
                byteContent: () => Uint8Array<ArrayBuffer> | null = () => this.content ? new Uint8Array(Buffer.from(this.content)) : null;
                children?: FSO[] | null;

                constructor(name: string, content: string | null = null, children: FSO[] | null = null) {
                    this.name = name;
                    this.content = content;
                    this.children = children;
                }

                static create(name: string, content: string | null = null, children: FSO[] | null = null) {
                    return new FSO(name, content, children);
                }

                static createFromNestedType(nestedType: FSONestedType[]) {
                    let output: FSO[] = [];

                    nestedType.forEach(item => {
                        let current = this.create(item.name, item.content);

                        if (!item.children) {
                            output.push(current);
                            return;
                        }

                        current.children = item.children.flatMap(child => this.createFromNestedType([child]));

                        output.push(current);
                    });

                    return output;
                }
            }

            const files: FSO[] = FSO.createFromNestedType(
                [
                    {
                        name: 'dir1',
                        content: null,
                        children: [
                            {
                                name: 'file.txt',
                                content: 'Hello there!'
                            },
                            {
                                name: 'source.lua',
                                content: 'print("Hello World!")'
                            }
                        ]
                    }
                ]
            );

            function ensureDir(guestPath: string): number {
                const fs9p = (emulator as any).fs9p;
                if (!fs9p) return -1;

                const search = fs9p.SearchPath(guestPath);
                if (search.id !== -1) {
                    return search.id;
                }

                const parts = guestPath.split('/').filter(Boolean);
                const name = parts.pop();
                if (!name) return 0;

                const parentPath = "/" + parts.join("/");
                const parentId = ensureDir(parentPath);

                return fs9p.CreateDirectory(name, parentId);
            }

            function syncDir(dir: FSO[], guestPath: string) {
                ensureDir(guestPath);

                for (const fso of dir) {
                    if (fso.children) {
                        syncDir(fso.children, guestPath + "/" + fso.name);
                    } else if (fso.content !== null) {
                        const guestFile = (guestPath + "/" + fso.name).replace(/\/\//g, "/");
                        const data = fso.byteContent();
                        if (data) {
                            emulator.create_file(guestFile, data).catch((err: any) => console.error("error:", err));
                        }
                    }
                }
            }

            syncDir(files, "/");

            const fs9p = (emulator as any).fs9p;
            const pathCache = new Map<number, string>();
            const logFilePath = path.join(__dirname, "file_op.txt");

            // Clear existing log file on startup
            if (fs.existsSync(logFilePath)) {
                fs.unlinkSync(logFilePath);
            }

            function getPath(id: number): string {
                if (pathCache.has(id)) {
                    return pathCache.get(id)!;
                }
                if (!fs9p) return "";

                let resolvedPath = "";
                if (fs9p.IsDirectory(id)) {
                    resolvedPath = fs9p.GetFullPath(id);
                } else {
                    for (let i = 0; i < fs9p.inodes.length; i++) {
                        const inode = fs9p.inodes[i];
                        if (inode && fs9p.IsDirectory(i)) {
                            for (const [name, childId] of inode.direntries) {
                                if (childId === id) {
                                    const parentPath = fs9p.GetFullPath(i);
                                    resolvedPath = parentPath + (parentPath === "/" ? "" : "/") + name;
                                    break;
                                }
                            }
                        }
                        if (resolvedPath) break;
                    }
                }
                if (resolvedPath) {
                    pathCache.set(id, resolvedPath);
                }
                return resolvedPath;
            }

            // Initialize path cache for pre-populated files/dirs
            function initPathCache() {
                if (!fs9p) return;
                for (let i = 0; i < fs9p.inodes.length; i++) {
                    if (fs9p.inodes[i]) {
                        getPath(i);
                    }
                }
            }

            initPathCache();

            fs9p.NotifyListeners = (id: number, event: string, metadata?: any) => {
                const timestamp = new Date().toISOString();
                let logLine = "";

                if (event === "newfile") {
                    const filePath = getPath(id);
                    logLine = `[${timestamp}] CREATE_FILE: ${filePath}\n`;
                } else if (event === "newdir") {
                    const dirPath = getPath(id);
                    logLine = `[${timestamp}] CREATE_DIR: ${dirPath}\n`;
                } else if (event === "rename") {
                    const oldPath = metadata?.oldpath || pathCache.get(id) || "unknown";
                    // Evict old path and any of its child paths from the cache
                    pathCache.delete(id);
                    if (oldPath !== "unknown") {
                        for (const [cachedId, cachedPath] of pathCache.entries()) {
                            if (cachedPath === oldPath || cachedPath.startsWith(oldPath + "/")) {
                                pathCache.delete(cachedId);
                            }
                        }
                    }
                    const newPath = getPath(id);
                    logLine = `[${timestamp}] RENAME: ${oldPath} -> ${newPath}\n`;
                } else if (event === "delete") {
                    const deletedPath = pathCache.get(id) || "unknown";
                    pathCache.delete(id);
                    logLine = `[${timestamp}] DELETE: ${deletedPath}\n`;
                }

                if (logLine) {
                    fs.appendFileSync(logFilePath, logLine);
                }
            };

            emulator.add_listener("9p-write-end", ([filename, byteCount]: any) => {
                const timestamp = new Date().toISOString();
                emulator.read_file(filename).then((data: any) => {
                    const content = Buffer.from(data).toString("utf8");
                    const escapedContent = content.replace(/\r?\n/g, "\\n");
                    const logLine = `[${timestamp}] WRITE: ${filename} (content: "${escapedContent}")\n`;
                    fs.appendFileSync(logFilePath, logLine);
                }).catch((err: any) => {
                    console.error("Failed to read file in 9p-write-end:", err);
                });
            });

            setTimeout(() => {
                emulator.serial0_send("root\n");

                // Auto-mount 9p after login
                setTimeout(() => {
                    emulator.serial0_send("mkdir -p /mnt/9p && mount -t 9p -o trans=virtio,version=9p2000.L host9p /mnt/9p/\n");
                    emulator.serial0_send("ls -la /mnt/9p/\n");

                    // Run test file operations after mounting
                    // setTimeout(() => {
                    //     emulator.serial0_send("echo '=== Testing File Operations ==='\n");
                    //     emulator.serial0_send("touch /mnt/9p/new_file.txt\n");
                    //     emulator.serial0_send("echo 'Hello Host' > /mnt/9p/new_file.txt\n");
                    //     emulator.serial0_send("mv /mnt/9p/new_file.txt /mnt/9p/moved_file.txt\n");
                    //     emulator.serial0_send("rm /mnt/9p/moved_file.txt\n");
                    //     emulator.serial0_send("echo '=== Testing Done ==='\n");
                    // }, 1000);
                }, 500);
            }, 1000);
        }
    });

    return {
        send: (c: string) => {
            emulator.serial0_send(c);
        },
        destroy: () => {
            emulator.destroy();
        }
    };
}