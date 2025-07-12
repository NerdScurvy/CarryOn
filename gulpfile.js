const gulp = require('gulp');
const replace = require('gulp-replace');
const fs = require('fs');

const carryOnVersion = require('./CarryOn.json').carryOnVersion;
const vintageStoryVersion = require('./CarryOn.json').vintageStoryVersion;

async function helloWorld() {
    console.log('Hello, World!');
    return true;
}

async function setVersion() {
    console.log(`Setting carryOnVersion to: ${carryOnVersion} and vintageStoryVersion to: ${vintageStoryVersion}`);

    if (!carryOnVersion) {
        console.error('No carryOnVersion specified. Update CarryOn.json with the carryOnVersion field.');
        return Promise.reject(new Error('carryOnVersion not specified'));
    }

    if (!vintageStoryVersion) {
        console.error('No vintageStoryVersion specified. Update CarryOn.json with the vintageStoryVersion field.');
        return Promise.reject(new Error('vintageStoryVersion not specified'));
    }

    try {
        await new Promise((resolve, reject) => {
            gulp.src('./resources/modinfo.json')
                .pipe(replace(/"version"\s*:\s*".*?"/, `"version": "${carryOnVersion}"`))
                .pipe(replace(/"game"\s*:\s*".*?"/, `"game": "${vintageStoryVersion}"`))
                .pipe(gulp.dest('./resources/'))
                .on('end', () => {
                    console.log('Version updated successfully in modinfo.json');
                    resolve();
                })
                .on('error', reject);
        });

        await new Promise((resolve, reject) => {
            gulp.src('./CarryOn.csproj')
                .pipe(replace(/<Version>.*?<\/Version>/, `<Version>${carryOnVersion}</Version>`))
                .pipe(gulp.dest('./'))
                .on('end', () => {
                    console.log('Version updated successfully in CarryOn.csproj');
                    resolve();
                })
                .on('error', reject);
        });

        await new Promise((resolve, reject) => {
            gulp.src('./src/CarrySystem.cs')
                .pipe(replace(/(Version\s*=\s*")[^"]*(")/, `$1${carryOnVersion}$2`))
                .pipe(replace(
                    /\[assembly:\s*ModDependency\(\s*"game"\s*,\s*"(.*?)"\s*\)\]/,
                    `[assembly: ModDependency("game", "${vintageStoryVersion}")]`
                ))
                .pipe(gulp.dest('./src/'))
                .on('end', () => {
                    console.log('Version updated successfully in CarrySystem.cs');
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
    const carryOnVersion = require('./CarryOn.json').carryOnVersion;
    const vintageStoryVersion = require('./CarryOn.json').vintageStoryVersion;

    if (!carryOnVersion || !vintageStoryVersion) {
        console.error('Cannot rename zip: carryOnVersion or vintageStoryVersion is not set.');
        return Promise.reject(new Error('carryOnVersion or vintageStoryVersion not set'));
    }

    const oldName = `CarryOn.zip`;
    const newName = `CarryOn-${vintageStoryVersion}_v${carryOnVersion}.zip`;
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
