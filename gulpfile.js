const gulp = require('gulp');
const replace = require('gulp-replace');
const fs = require('fs');

const VERSION_JSON_PATH = './version.json';
const versionData = require(VERSION_JSON_PATH);
const modVersion = versionData.modVersion;
const dependencies = versionData.dependencies || {};

async function helloWorld() {
    console.log('Hello, World!');
    return true;
}

async function setVersion() {
    console.log(`Setting modVersion to: ${modVersion} and dependencies: ${JSON.stringify(dependencies)}`);

    if (!modVersion) {
        console.error('No modVersion specified. Update version.json with the modVersion field.');
        return Promise.reject(new Error('modVersion not specified'));
    }

    // Check all dependencies have a version
    for (const dep in dependencies) {
        if (!dependencies[dep]) {
            console.error(`No version specified for dependency '${dep}' in version.json.`);
            return Promise.reject(new Error(`Dependency '${dep}' version not specified`));
        }
    }

    try {
        await new Promise((resolve, reject) => {
            gulp.src('./resources/modinfo.json')
                .pipe(replace(/"version"\s*:\s*".*?"/, `"version": "${modVersion}"`))
                .pipe(replace(/"(.*?)"\s*:\s*"(.*?)"/g, (match, dep, ver) => {
                    if (dependencies.hasOwnProperty(dep)) {
                        return `"${dep}": "${dependencies[dep]}"`;
                    }
                    return match;
                }))
                .pipe(gulp.dest('./resources/'))
                .on('end', () => {
                    console.log('Version and dependencies updated successfully in modinfo.json');
                    resolve();
                })
                .on('error', reject);
        });

        await new Promise((resolve, reject) => {
            gulp.src('./CarryOn.csproj')
                .pipe(replace(/<Version>.*?<\/Version>/, `<Version>${modVersion}</Version>`))
                .pipe(gulp.dest('./'))
                .on('end', () => {
                    console.log('Version updated successfully in CarryOn.csproj');
                    resolve();
                })
                .on('error', reject);
        });

        await new Promise((resolve, reject) => {
            gulp.src('./src/CarrySystem.cs')
                .pipe(replace(/(Version\s*=\s*")[^"]*(")/, `$1${modVersion}$2`))
                .pipe(replace(/\[assembly:\s*ModDependency\(\s*"(.*?)"\s*,\s*"(.*?)"\s*\)\]/g, (match, dep, ver) => {
                    if (dependencies.hasOwnProperty(dep)) {
                        return `[assembly: ModDependency("${dep}", "${dependencies[dep]}")]`;
                    }
                    return match;
                }))
                .pipe(gulp.dest('./src/'))
                .on('end', () => {
                    console.log('Version and dependencies updated successfully in CarrySystem.cs');
                    resolve();
                })
                .on('error', reject);
        });

        console.log('Version update completed successfully.');
        return true;
    } catch (error) {
        console.error('One or more version updates failed:', error.message);
        throw error;
    }
}

function renameZip() {
    const vintageStoryVersion = versionData.dependencies.game;

    if (!modVersion || !vintageStoryVersion) {
        console.error('Cannot rename zip: modVersion or dependencies.game is not set in version.json.');
        return Promise.reject(new Error('modVersion or dependencies.game not set in version.json'));
    }

    const oldName = `CarryOn.zip`;
    const newName = `CarryOn-${vintageStoryVersion}_v${modVersion}.zip`;
    const destinationPath = `./release`;
    const sourcePath = `./bin`;


    return new Promise((resolve, reject) => {
        if( !fs.existsSync(`${sourcePath}/${oldName}`)) {
            console.log(`Release has not been built yet, skipping rename.`);
            return resolve();
        }        
        fs.mkdir(destinationPath, { recursive: true }, (err) => {
            if (err) {
                console.error(`Error creating release directory: ${err.message}`);
                return reject(err);
            }
        });        
        if( fs.existsSync(`${destinationPath}/${newName}`)) {
            console.log(`Release file already exists: ${newName}`);
            return reject(new Error(`Release file already exists: ${newName}`));
        }
        fs.rename(`${sourcePath}/${oldName}`, `./release/${newName}`, (err) => {
            if (err) {
                console.error(`Error renaming zip file: ${err.message}`);
                return reject(err);
            }
            console.log(`Renamed ${oldName} to ${newName} and moved to release folder`);
            resolve();
        });
    });
}

gulp.task('set-version', setVersion);
gulp.task('rename-zip', renameZip);
gulp.task('hello-world', helloWorld);
