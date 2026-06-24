import { defineConfig } from 'tsup';

export default defineConfig((options) => ({
    entry: ['index.ts'],
    format: ['cjs'],
    platform: 'node',
    target: 'node20',
    bundle: true,
    splitting: false,
    sourcemap: !!options.watch,
    minify: false,
    clean: true,
    shims: true,
    outDir: 'dist',
    outExtension: () => ({ js: '.cjs' }),
    noExternal: [/.*/],
    loader: {
        '.svg': 'text',
    },
}));