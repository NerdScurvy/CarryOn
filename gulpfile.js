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

gulp.task('set-version', setVersion);
gulp.task('hello-world', helloWorld);
