import http from "node:http";

const s = http.createServer((req, res) => {
    res.statusCode = 404;
    res.setHeader("x-error-penalty", "10");
    res.end();
});

s.listen(Number(process.env.PORT));

const host = process.env.TEST_HOST;

async function test() {
    try {
        const rs = await fetch(`https://${host}`);
        if(rs.status > 399) {
            throw new Error(`status: ${rs.status}`);
        }
        const body = await rs.text();
    } catch (error) {
        console.error(error.cause ? error.cause.stack || error.cause : error);
    }
}

async function runAll() {
    while(true) {
        const all = [];
        for(let i=0;i<1000;i++) {
            all.push(test());
        }
        await Promise.all(all);
    }
}

runAll().catch(console.error);